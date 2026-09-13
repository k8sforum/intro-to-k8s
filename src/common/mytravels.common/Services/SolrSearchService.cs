using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
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

            List<KeyValuePair<string, string>> parameters =
            [
                new("q", string.IsNullOrWhiteSpace(query.Term) ? "*:*" : query.Term),
                new("defType", "edismax"),
                new("qf", QueryFields),
                new("rows", (query.Rows <= 0 ? 100 : query.Rows).ToString(CultureInfo.InvariantCulture)),
                new("start", (query.Start < 0 ? 0 : query.Start).ToString(CultureInfo.InvariantCulture)),
                new("wt", "json")
            ];

            if (!string.IsNullOrWhiteSpace(query.Tag))
            {
                parameters.Add(new KeyValuePair<string, string>("fq", $"tag_exact:\"{EscapeQuoted(query.Tag)}\""));
            }

            if (query.From.HasValue || query.To.HasValue)
            {
                parameters.Add(new KeyValuePair<string, string>("fq", BuildDateFilter(query.From, query.To)));
            }

            JObject response = await _client.SelectAsync(parameters, cancellationToken);
            JToken documents = response["response"]?["docs"];

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

        /// <summary>
        /// Turns one SOLR document into one response row per tag, or a single row with a null tag when the
        /// document carries none — the exact shape spGetPointOfInterest() produces.
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

        /// <summary>
        /// Filters on the capture date, falling back to the creation date for points that have none, so a
        /// date-filtered search does not silently drop every photo without EXIF date metadata.
        /// </summary>
        private static string BuildDateFilter(DateTime? from, DateTime? to)
        {
            string lower = from.HasValue ? FormatDate(from.Value) : "*";
            string upper = to.HasValue ? FormatDate(to.Value) : "*";

            return $"date_taken:[{lower} TO {upper}] OR (-date_taken:[* TO *] AND date_created:[{lower} TO {upper}])";
        }

        private static string FormatDate(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();

            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        private static string EscapeQuoted(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
