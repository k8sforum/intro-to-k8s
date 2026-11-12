using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Globalization;
using mytravels.contract.Dtos;
using mytravels.contract.Interfaces;
using mytravels.contract.Responses;

namespace mytravels.common.Services
{
    public class SolrSearchService : ISolrSearchService
    {
        // Boosts are query-time parameters, so relevance can be retuned without reindexing or a schema change.
        private const string QueryFields = "formatted_address^5 tags^3 description^1";

        // Rewritten searches target each half of the term at its own fields. The text itself is never spliced into
        // query syntax: it travels in the loc/kw parameters and is pulled in by v=$loc / v=$kw, and uf=-* stops
        // edismax honouring field:value syntax inside it. mm=100% makes every token of the place name match
        // (so "cape town" does not match every "town"); mm=1 lets any one keyword match.
        private const string LocationQuery = "{!edismax qf=formatted_address^5 mm=100% uf=-* v=$loc}";
        private const string KeywordQuery = "{!edismax qf='tags^3 description^1' mm=1 uf=-* v=$kw}";

        // Every search writes one line under this event id, so rewritten searches can be evaluated against the
        // term they came from: grep the api (or mcp) log for "SOLR search" and every field needed is on that line.
        private static readonly EventId SearchExecuted = new(1001, "SolrSearchExecuted");
        private const int LoggedResultIds = 10;

        // Which query produced the results. "both" requires the location AND the keywords; "relaxed" is the OR
        // it falls back to when "both" found nothing; "location" and "keywords" are rewrites with only one half.
        private const string RawStrategy = "raw";
        private const string LocationStrategy = "location";
        private const string KeywordsStrategy = "keywords";
        private const string BothStrategy = "both";
        private const string RelaxedStrategy = "relaxed";

        private readonly SolrClient _client;
        private readonly ILogger<SolrSearchService> _logger;

        public SolrSearchService(SolrClient client, ILogger<SolrSearchService> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<List<GetPointOfInterestResponse>> SearchAsync(SolrSearchQuery query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);

            long started = Stopwatch.GetTimestamp();

            SolrQueryRun run = query.Rewrite is { } rewrite && HasContent(rewrite)
                ? await SearchRewrittenAsync(query, rewrite, cancellationToken)
                : await RunAsync(RawStrategy, RawTermParameters(query), cancellationToken);

            LogSearch(query, run, Stopwatch.GetElapsedTime(started));

            JToken documents = run.Response["response"]?["docs"];

            if (documents is null)
            {
                _logger.LogWarning("SOLR returned no response body for term '{Term}'.", query.Term);
                return new List<GetPointOfInterestResponse>();
            }

            var results = new List<GetPointOfInterestResponse>();
            int rowId = 1;

            foreach (JToken document in documents)
            {
                results.AddRange(Expand(document, ref rowId));
            }

            return results;
        }

        public async Task<List<string>> GetTopTagsAsync(int limit, CancellationToken cancellationToken)
        {
            // Facet on tag_exact rather than tags: tags is tokenised, so its facet would return tokens, not tag names.
            List<KeyValuePair<string, string>> parameters =
            [
                new("q", "*:*"),
                new("rows", "0"),
                new("facet", "true"),
                new("facet.field", "tag_exact"),
                new("facet.limit", (limit <= 0 ? 100 : limit).ToString(CultureInfo.InvariantCulture)),
                new("facet.mincount", "1"),
                new("facet.sort", "count"),
                new("wt", "json")
            ];

            JObject response = await _client.SelectAsync(parameters, cancellationToken);

            // json.nl defaults to "flat": ["beach", 12, "ocean", 9, ...], so the names sit at the even indexes.
            if (response["facet_counts"]?["facet_fields"]?["tag_exact"] is not JArray counts)
            {
                return new List<string>();
            }

            var tags = new List<string>();
            for (int i = 0; i < counts.Count; i += 2)
            {
                string name = counts[i]?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) tags.Add(name);
            }

            return tags;
        }

        private static List<KeyValuePair<string, string>> RawTermParameters(SolrSearchQuery query) =>
            WithPaging(query,
            [
                new("q", string.IsNullOrWhiteSpace(query.Term) ? "*:*" : query.Term),
                new("defType", "edismax"),
                new("qf", QueryFields)
            ]);

        /// <summary>
        /// Searches a rewritten term. With both a location and keywords it first asks for photos matching both
        /// (beaches in Cape Town), because the map draws every hit regardless of rank and an OR would put a pin
        /// on every beach anywhere plus every Cape Town photo. Only if that finds nothing does it relax to an OR
        /// with the location boosted, so an over-specific rewrite still returns the closest matches.
        /// </summary>
        private async Task<SolrQueryRun> SearchRewrittenAsync(SolrSearchQuery query, SearchTermRewrite rewrite, CancellationToken cancellationToken)
        {
            string strategy = (HasLocation(rewrite), HasKeywords(rewrite)) switch
            {
                (true, true) => BothStrategy,
                (true, false) => LocationStrategy,
                _ => KeywordsStrategy
            };

            SolrQueryRun run = await RunAsync(strategy, RewrittenParameters(query, rewrite, requireBoth: true), cancellationToken);

            if (strategy != BothStrategy || NumFound(run.Response) > 0)
            {
                return run;
            }

            // Logged as strategy "relaxed" by LogSearch, which is how an empty "both" shows up in the search log.
            return await RunAsync(RelaxedStrategy, RewrittenParameters(query, rewrite, requireBoth: false), cancellationToken);
        }

        private async Task<SolrQueryRun> RunAsync(string strategy, List<KeyValuePair<string, string>> parameters, CancellationToken cancellationToken) =>
            new(strategy, parameters, await _client.SelectAsync(parameters, cancellationToken));

        /// <summary>
        /// One line per search with everything an evaluation needs: the original term, whether a rewrite was asked
        /// for, the location and keywords it produced, which query ran, what it found, and the exact parameters
        /// sent, URL-encoded so they replay as-is against <c>/solr/{collection}/select?</c>. A rewritten search can
        /// be compared with its raw term by replaying the term (strategy "raw" is just q=term over every field).
        /// </summary>
        private void LogSearch(SolrSearchQuery query, SolrQueryRun run, TimeSpan elapsed)
        {
            // The map's list is a term-less q=*:* that it polls every few seconds; only real searches are worth reading.
            LogLevel level = string.IsNullOrWhiteSpace(query.Term) ? LogLevel.Debug : LogLevel.Information;
            if (!_logger.IsEnabled(level)) return;

            SearchTermRewrite rewrite = run.Strategy == RawStrategy ? null : query.Rewrite;

            string topIds = run.Response["response"]?["docs"] is JArray documents
                ? string.Join(", ", documents.Take(LoggedResultIds).Select(document => document["poi_id"]?.ToString()))
                : string.Empty;

            string parameters = string.Join('&', run.Parameters.Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value ?? string.Empty)}"));

            _logger.Log(
                level,
                SearchExecuted,
                "SOLR search {Strategy} in {ElapsedMs}ms: '{Term}' rewriteRequested={RewriteRequested} location={Location} keywords=[{Keywords}] -> numFound={NumFound} topIds=[{TopIds}] params={Parameters}",
                run.Strategy,
                (long)elapsed.TotalMilliseconds,
                query.Term,
                query.RewriteTerm,
                rewrite?.Location,
                rewrite is null ? string.Empty : string.Join(", ", rewrite.Keywords),
                NumFound(run.Response),
                topIds,
                parameters);
        }

        /// <summary>The SOLR request whose response a search returns, kept so <see cref="LogSearch"/> can record it.</summary>
        private sealed record SolrQueryRun(string Strategy, List<KeyValuePair<string, string>> Parameters, JObject Response);

        private static List<KeyValuePair<string, string>> RewrittenParameters(SolrSearchQuery query, SearchTermRewrite rewrite, bool requireBoth)
        {
            bool hasLocation = HasLocation(rewrite);
            bool hasKeywords = HasKeywords(rewrite);

            string q = (hasLocation, hasKeywords) switch
            {
                (true, true) when requireBoth => "{!bool must=$locq must=$kwq}",
                (true, true) => "{!bool should=$locq should=$kwq}",
                (true, false) => LocationQuery,
                _ => KeywordQuery
            };

            // No defType here, unlike RawTermParameters: Solr only honours a leading {!...} in q under the default
            // lucene parser, so setting defType=edismax would make it search for the local-params text literally.
            List<KeyValuePair<string, string>> parameters = [new("q", q)];

            if (hasLocation)
            {
                parameters.Add(new("locq", LocationQuery));
                parameters.Add(new("loc", rewrite.Location.Trim()));
            }

            if (hasKeywords)
            {
                parameters.Add(new("kwq", KeywordQuery));
                parameters.Add(new("kw", string.Join(' ', rewrite.Keywords.Where(k => !string.IsNullOrWhiteSpace(k)))));
            }

            return WithPaging(query, parameters);
        }

        private static List<KeyValuePair<string, string>> WithPaging(SolrSearchQuery query, List<KeyValuePair<string, string>> parameters)
        {
            parameters.Add(new("rows", (query.Rows <= 0 ? 100 : query.Rows).ToString(CultureInfo.InvariantCulture)));
            parameters.Add(new("start", (query.Start < 0 ? 0 : query.Start).ToString(CultureInfo.InvariantCulture)));
            parameters.Add(new("wt", "json"));
            return parameters;
        }

        private static bool HasContent(SearchTermRewrite rewrite) => HasLocation(rewrite) || HasKeywords(rewrite);

        private static bool HasLocation(SearchTermRewrite rewrite) => !string.IsNullOrWhiteSpace(rewrite.Location);

        private static bool HasKeywords(SearchTermRewrite rewrite) =>
            rewrite.Keywords is { } keywords && keywords.Any(k => !string.IsNullOrWhiteSpace(k));

        private static long NumFound(JObject response) => response["response"]?["numFound"]?.Value<long>() ?? 0;

        /// <summary>
        /// Turns one SOLR document into one response row per tag, or a single row with a null tag when the
        /// document carries none - the exact shape spGetPointOfInterest() produces.
        /// </summary>
        private static IEnumerable<GetPointOfInterestResponse> Expand(JToken document, ref int rowId)
        {
            List<string> tagNames = ReadArray(document, "tags");
            List<string> tagIds = ReadArray(document, "tag_ids");

            var rows = new List<GetPointOfInterestResponse>();
            int tagCount = Math.Min(tagNames.Count, tagIds.Count);

            if (tagCount == 0)
            {
                rows.Add(MapRow(document, rowId++, null, null));
                return rows;
            }

            for (int i = 0; i < tagCount; i++)
            {
                int? tagId = int.TryParse(tagIds[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
                rows.Add(MapRow(document, rowId++, tagId, tagNames[i]));
            }

            return rows;
        }

        private static GetPointOfInterestResponse MapRow(JToken document, int rowId, int? tagId, string tagName)
        {
            return new GetPointOfInterestResponse
            {
                // RowId is the EF key of this type, so it has to stay unique across the whole result set.
                RowId = rowId,
                PointOfInterestId = document["poi_id"]?.Value<int>() ?? 0,
                Container = document["container"]?.ToString(),
                OriginalFileName = document["original_file_name"]?.ToString(),
                GeneratedBlobName = document["generated_blob_name"]?.ToString(),
                Latitude = document["latitude"]?.Value<double>() ?? 0,
                Longitude = document["longitude"]?.Value<double>() ?? 0,
                DateCreated = ParseDate(document["date_created"]) ?? default,
                DateTaken = ParseDate(document["date_taken"]),
                FormattedAddress = document["formatted_address"]?.ToString(),
                Description = document["description"]?.ToString(),
                ImageResized = document["image_resized"]?.Value<bool>() ?? false,
                PointOfInterestKey = document["id"]?.ToString(),
                CorrelationId = Guid.TryParse(document["correlation_id"]?.ToString(), out Guid correlationId) ? correlationId : null,
                TagId = tagId,
                TagName = tagName
            };
        }

        /// <summary>Parses a SOLR date, always landing on a UTC DateTime as PostgreSQL used to return.</summary>
        private static DateTime? ParseDate(JToken value)
        {
            string raw = value?.ToString();
            if (string.IsNullOrWhiteSpace(raw)) return null;

            return DateTime.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime parsed)
                ? parsed
                : null;
        }

        private static List<string> ReadArray(JToken document, string field)
        {
            JToken value = document[field];

            if (value is JArray array)
            {
                return array.Select(item => item?.ToString()).Where(item => item is not null).ToList();
            }

            return value is null ? new List<string>() : new List<string> { value.ToString() };
        }
    }
}
