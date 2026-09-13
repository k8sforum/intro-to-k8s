using Flurl;
using Flurl.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;
using mytravels.contract.Config;

namespace mytravels.common.Services
{
    /// <summary>
    /// Thin HTTP wrapper over the SOLR handlers the app uses. SOLR's /select, /update and /schema are
    /// plain JSON over HTTP, so this follows the Flurl convention the maps services already use rather
    /// than pulling in a client library.
    /// </summary>
    public class SolrClient
    {
        private readonly SolrConfig _config;
        private readonly ILogger<SolrClient> _logger;
        private readonly int _maxRetryAttempts = 2;
        private readonly AsyncRetryPolicy _policy;

        public SolrClient(IOptions<SolrConfig> options, ILogger<SolrClient> logger)
        {
            _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _policy = Policy
                .Handle<FlurlHttpTimeoutException>()
                .Or<FlurlHttpException>()
                .WaitAndRetryAsync(
                    retryCount: _maxRetryAttempts,
                    sleepDurationProvider: i => TimeSpan.FromSeconds(Math.Pow(2, i)),
                    onRetry: (exception, timespan, retryCount, context) =>
                    {
                        var exceptionMessage = exception?.Message ?? "timeout";
                        _logger.LogWarning(
                            "SOLR retry attempt {AttemptNumber} after {DelayMs}ms due to {Exception}",
                            retryCount,
                            timespan.TotalMilliseconds,
                            exceptionMessage
                        );
                    });
        }

        public SolrConfig Config => _config;

        /// <summary>Runs a query against the /select handler. Parameters may repeat a name (fq, for example).</summary>
        public async Task<JObject> SelectAsync(IEnumerable<KeyValuePair<string, string>> parameters, CancellationToken cancellationToken)
        {
            return await _policy.ExecuteAsync(async () =>
            {
                Url url = CollectionUrl().AppendPathSegment("select");
                foreach (KeyValuePair<string, string> parameter in parameters)
                {
                    url = url.AppendQueryParam(parameter.Key, parameter.Value);
                }

                string response = await url.WithTimeout(Timeout)
                                           .GetStringAsync(cancellationToken: cancellationToken);
                return ParseWithoutDateConversion(response);
            });
        }

        /// <summary>
        /// Leaves date-looking strings as strings. Newtonsoft would otherwise turn them into DateTime
        /// tokens whose Kind depends on the machine's time zone, and the callers want UTC.
        /// </summary>
        private static JObject ParseWithoutDateConversion(string json)
        {
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader);
        }

        /// <summary>Posts an update command (add, delete, …) and commits it.</summary>
        public async Task UpdateAsync(object payload, CancellationToken cancellationToken)
        {
            await _policy.ExecuteAsync(async () =>
                await CollectionUrl().AppendPathSegment("update")
                                     .SetQueryParam("commit", "true")
                                     .WithTimeout(Timeout)
                                     .PostJsonAsync(payload, cancellationToken: cancellationToken));
        }

        /// <summary>Returns the names of every field currently defined on the collection.</summary>
        public async Task<HashSet<string>> GetSchemaFieldNamesAsync(CancellationToken cancellationToken)
        {
            // Deliberately un-retried: the schema initializer owns the backoff for a SOLR that is not up yet.
            string response = await CollectionUrl().AppendPathSegment("schema")
                                                   .AppendPathSegment("fields")
                                                   .WithTimeout(Timeout)
                                                   .GetStringAsync(cancellationToken: cancellationToken);

            JObject json = JObject.Parse(response);
            return json["fields"]?
                       .Select(field => field["name"]?.ToString())
                       .Where(name => !string.IsNullOrEmpty(name))
                       .ToHashSet()
                   ?? new HashSet<string>();
        }

        /// <summary>Posts a Schema API command such as add-field.</summary>
        public async Task PostSchemaAsync(object payload, CancellationToken cancellationToken)
        {
            await CollectionUrl().AppendPathSegment("schema")
                                 .WithTimeout(Timeout)
                                 .PostJsonAsync(payload, cancellationToken: cancellationToken);
        }

        private Url CollectionUrl() => _config.Url.AppendPathSegment("solr").AppendPathSegment(_config.Collection);

        private TimeSpan Timeout => TimeSpan.FromSeconds(_config.TimeoutSeconds <= 0 ? 30 : _config.TimeoutSeconds);
    }
}
