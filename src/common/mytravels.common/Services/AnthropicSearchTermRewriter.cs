using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using mytravels.contract.Dtos;
using mytravels.contract.Interfaces;

namespace mytravels.common.Services
{
    /// <summary>
    /// Turns a natural-language search ("beach photos from Cape Town") into the location and keywords the SOLR
    /// index can match, using Claude with structured output. Built to sit in the request path of a search box
    /// that fires on every debounced keystroke, so it is cheap to skip, cached, bounded by a hard time budget,
    /// and never fails a search: every problem degrades to "search the raw term".
    /// Registered as a singleton so one AnthropicClient (and its HttpClient) serves every request.
    /// </summary>
    public class AnthropicSearchTermRewriter : ISearchTermRewriter
    {
        public const string ActivitySourceName = "MyTravels.Search";
        private static readonly ActivitySource _activitySource = new(ActivitySourceName);

        private const string DefaultModel = "claude-haiku-4-5";
        private const string UnsetAnthropicApiKey = "<YOUR_ANTHROPIC_API_KEY>";

        // Terms of one or two words go straight to SOLR: there is nothing to separate or drop, and they are
        // what the search box sends while someone is still typing.
        private const int MinWordsToRewrite = 3;
        private const int MaxKeywords = 6;
        private const int VocabularySize = 100;

        // The whole rewrite (tag lookup plus model call) must finish inside Budget or the raw term is searched.
        // The tag lookup gets a slice of it so a slow SOLR still leaves the model time to answer.
        private static readonly TimeSpan Budget = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan VocabularyBudget = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan RewriteCacheTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan VocabularyCacheTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan VocabularyFailureCacheTtl = TimeSpan.FromMinutes(1);

        private const string RewriteCacheKeyPrefix = "nlp-search:rewrite:";
        private const string VocabularyCacheKey = "nlp-search:vocabulary";

        private const string SystemPrompt =
            "You convert what someone typed into the search box of their personal travel-photo library into a " +
            "structured search. Return JSON only.\n\n" +
            "Every photo is indexed with three searchable fields:\n" +
            "- formatted_address: the geocoded address where it was taken, e.g. \"Victoria Rd, Camps Bay, Cape Town, 8005, South Africa\".\n" +
            "- tags: 3-6 lowercase single-word labels for what is visible, e.g. beach, ocean, mountain, road, car.\n" +
            "- description: one short factual sentence about what is visible. It never names a place.\n\n" +
            "Fill two fields:\n" +
            "- location: the place the person wants photos from (country, region, city, suburb, street or named " +
            "landmark), as they wrote it, without words like \"in\", \"from\" or \"near\". null when no place is named.\n" +
            "- keywords: 0-6 lowercase single words for what should be visible in the photo.\n\n" +
            "Choosing keywords:\n" +
            "- Leave out words about the library or the request rather than the scene: photo, photos, picture, " +
            "pic, image, shot, my, that, those, the, a, from, in, at, of, show, find, me, some, trip, holiday.\n" +
            "- When a known tag (listed at the end, if any) means the same as the person's word, use the known tag " +
            "(seaside -> beach, sea -> ocean). Keep the person's word as well when it is more specific.\n" +
            "- Use the singular (waterfalls -> waterfall).\n" +
            "- Never put a place name in keywords or a scene word in location. Don't add places or scenes the " +
            "person didn't ask for.\n\n" +
            "Examples:\n" +
            "\"beach photos from Cape Town\" -> {\"location\": \"Cape Town\", \"keywords\": [\"beach\"]}\n" +
            "\"that waterfall hike\" -> {\"location\": null, \"keywords\": [\"waterfall\", \"hike\"]}\n" +
            "\"sunset over the harbour in Hout Bay\" -> {\"location\": \"Hout Bay\", \"keywords\": [\"sunset\", \"harbour\"]}";

        private static readonly Dictionary<string, JsonElement> OutputSchema = new()
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(new
            {
                location = new { anyOf = new object[] { new { type = "string" }, new { type = "null" } } },
                keywords = new { type = "array", items = new { type = "string" } }
            }),
            ["required"] = JsonSerializer.SerializeToElement(new[] { "location", "keywords" }),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false)
        };

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly AnthropicClient _client;
        private readonly string _model;
        private readonly ISolrSearchService _solrSearchService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AnthropicSearchTermRewriter> _logger;

        public AnthropicSearchTermRewriter(
            IConfiguration configuration,
            ISolrSearchService solrSearchService,
            IMemoryCache cache,
            ILogger<AnthropicSearchTermRewriter> logger)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            _solrSearchService = solrSearchService ?? throw new ArgumentNullException(nameof(solrSearchService));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            string apiKey = configuration.GetValue<string>("AnthropicApiKey");
            if (!string.IsNullOrEmpty(apiKey) && apiKey != UnsetAnthropicApiKey)
            {
                // No SDK retries: a retry cannot fit inside the budget, and the fallback is the raw term anyway.
                _client = new AnthropicClient { ApiKey = apiKey, MaxRetries = 0, Timeout = Budget };
            }

            string model = configuration.GetValue<string>("AnthropicModel");
            _model = string.IsNullOrEmpty(model) ? DefaultModel : model;
        }

        public async Task<SearchTermRewrite> RewriteAsync(string term, CancellationToken cancellationToken)
        {
            string collapsed = CollapseWhitespace(term);
            if (collapsed.Length == 0) return null;

            using Activity activity = _activitySource.StartActivity("search.rewrite");
            activity?.SetTag("search.term", collapsed);
            long started = Stopwatch.GetTimestamp();

            try
            {
                (SearchTermRewrite rewrite, string outcome) = await RewriteCoreAsync(collapsed, cancellationToken);
                Record(activity, collapsed, rewrite, outcome, Stopwatch.GetElapsedTime(started));
                return rewrite;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                activity?.SetTag("search.rewrite.outcome", "cancelled");
                throw;
            }
        }

        private async Task<(SearchTermRewrite Rewrite, string Outcome)> RewriteCoreAsync(string term, CancellationToken cancellationToken)
        {
            string normalised = term.ToLowerInvariant();

            if (normalised.Split(' ').Length < MinWordsToRewrite) return (null, "skipped");
            if (_client is null) return (null, "not_configured");

            string cacheKey = RewriteCacheKeyPrefix + normalised;
            if (_cache.TryGetValue(cacheKey, out SearchTermRewrite cached)) return (cached, "cache_hit");

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(Budget);

            try
            {
                List<string> vocabulary = await GetVocabularyAsync(budget.Token);
                (SearchTermRewrite rewrite, string outcome) = await CallModelAsync(term, vocabulary, budget.Token);

                // Only successes are cached, so a brief outage does not pin raw-term searches for the TTL.
                if (rewrite is not null) _cache.Set(cacheKey, rewrite, RewriteCacheTtl);
                return (rewrite, outcome);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller gave up (the web search box aborts its request on every keystroke): not a rewriter failure.
                throw;
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("The search was cancelled by the caller.", ex, cancellationToken);
            }
            catch (Exception ex) when (budget.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Search term rewrite exceeded {BudgetMs}ms for '{Term}'; searching the raw term.", Budget.TotalMilliseconds, term);
                return (null, "timeout");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Search term rewrite failed for '{Term}'; searching the raw term.", term);
                return (null, "error");
            }
        }

        private async Task<(SearchTermRewrite Rewrite, string Outcome)> CallModelAsync(string term, List<string> vocabulary, CancellationToken cancellationToken)
        {
            MessageCreateParams parameters = new()
            {
                Model = _model,
                MaxTokens = 256,
                System = BuildSystemPrompt(vocabulary),
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = $"<search>{term}</search>"
                    }
                ],
                OutputConfig = new OutputConfig
                {
                    Format = new JsonOutputFormat { Schema = OutputSchema }
                }
            };

            Message response = await _client.Messages.Create(parameters, cancellationToken: cancellationToken);

            if (response.StopReason == "refusal") return (null, "refused");

            string text = response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text
                ?? throw new InvalidOperationException($"The model returned no text (stop reason '{response.StopReason}').");

            SearchTermRewrite rewrite = Sanitise(JsonSerializer.Deserialize<SearchTermRewrite>(text, JsonOptions));

            return rewrite.Location is null && rewrite.Keywords.Count == 0
                ? (null, "empty")
                : (rewrite, "rewritten");
        }

        /// <summary>
        /// The most common tags in the index, so the model maps "seaside" to a tag that actually exists rather
        /// than one it guessed. Best effort: without them the prompt simply has no "Known tags" list.
        /// </summary>
        private async Task<List<string>> GetVocabularyAsync(CancellationToken cancellationToken)
        {
            if (_cache.TryGetValue(VocabularyCacheKey, out List<string> cached)) return cached;

            using var slice = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            slice.CancelAfter(VocabularyBudget);

            try
            {
                // WaitAsync bounds the wait even while SolrClient's retry policy is sleeping between attempts.
                List<string> tags = await _solrSearchService.GetTopTagsAsync(VocabularySize, slice.Token).WaitAsync(slice.Token);
                _cache.Set(VocabularyCacheKey, tags, VocabularyCacheTtl);
                return tags;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Only this lookup failed or ran out of its slice; the overall budget still has time for the model.
                _logger.LogWarning(ex, "Could not read the tag vocabulary from SOLR; rewriting without it.");
                _cache.Set(VocabularyCacheKey, new List<string>(), VocabularyFailureCacheTtl);
                return new List<string>();
            }
        }

        private static string BuildSystemPrompt(List<string> vocabulary)
        {
            if (vocabulary.Count == 0) return SystemPrompt;

            return new StringBuilder(SystemPrompt)
                .Append("\n\nKnown tags, most common first:\n")
                .AppendJoin(", ", vocabulary)
                .ToString();
        }

        /// <summary>Normalises the model's answer so the SOLR side never has to second-guess it.</summary>
        private static SearchTermRewrite Sanitise(SearchTermRewrite raw)
        {
            string location = raw?.Location?.Trim();
            if (string.IsNullOrEmpty(location) || location.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                location = null;
            }

            List<string> keywords = (raw?.Keywords ?? new List<string>())
                .Select(keyword => keyword?.Trim().ToLowerInvariant())
                .Where(keyword => !string.IsNullOrEmpty(keyword))
                .Distinct()
                .Take(MaxKeywords)
                .ToList();

            return new SearchTermRewrite { Location = location, Keywords = keywords };
        }

        private void Record(Activity activity, string term, SearchTermRewrite rewrite, string outcome, TimeSpan elapsed)
        {
            string keywords = rewrite is null ? string.Empty : string.Join(", ", rewrite.Keywords);

            activity?.SetTag("search.rewrite.outcome", outcome);
            activity?.SetTag("search.rewrite.model", _model);
            if (rewrite is not null)
            {
                activity?.SetTag("search.rewrite.location", rewrite.Location);
                activity?.SetTag("search.rewrite.keywords", keywords);
            }
            if (outcome is "timeout" or "error")
            {
                activity?.SetStatus(ActivityStatusCode.Error, outcome);
            }

            // Skips fire on almost every keystroke, so they stay out of the Information log.
            LogLevel level = outcome switch
            {
                "skipped" or "not_configured" => LogLevel.Debug,
                "rewritten" or "cache_hit" => LogLevel.Information,
                _ => LogLevel.Warning
            };

            _logger.Log(
                level,
                "Search term rewrite {Outcome} in {ElapsedMs}ms: '{Term}' -> location={Location} keywords=[{Keywords}]",
                outcome,
                (long)elapsed.TotalMilliseconds,
                term,
                rewrite?.Location,
                keywords);
        }

        private static string CollapseWhitespace(string term) =>
            string.IsNullOrWhiteSpace(term)
                ? string.Empty
                : string.Join(' ', term.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }
}
