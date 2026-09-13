using Flurl.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using mytravels.contract.Interfaces;
using mytravels.contract.Responses;
using mytravels.domain;

namespace mytravels.common.Services
{
    public class SolrIndexService : ISolrIndexService
    {
        private readonly SolrClient _client;
        private readonly ICoreDbContext _context;
        private readonly ILogger<SolrIndexService> _logger;

        /// <summary>
        /// The fields the app queries and reads back. Everything else about the collection is left to the
        /// configset SOLR ships with, so this is the single definition of the schema across all five stages.
        /// </summary>
        private static readonly SolrField[] Fields =
        [
            new("poi_id", "pint", false),
            new("formatted_address", "text_general", false),
            new("tags", "text_general", true),
            new("tag_exact", "string", true),
            new("tag_ids", "pint", true),
            new("description", "text_general", false),
            new("date_taken", "pdate", false),
            new("date_created", "pdate", false),
            new("latitude", "pdouble", false),
            new("longitude", "pdouble", false),
            new("container", "string", false),
            new("original_file_name", "string", false),
            new("generated_blob_name", "string", false),
            new("image_resized", "boolean", false),
            new("correlation_id", "string", false)
        ];

        public SolrIndexService(SolrClient client, ICoreDbContext context, ILogger<SolrIndexService> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task IndexAsync(int pointOfInterestId, CancellationToken cancellationToken)
        {
            contract.Entities.PointOfInterest point = await _context.PointOfInterests
                .FirstOrDefaultAsync(x => x.Id == pointOfInterestId, cancellationToken);

            if (point is null)
            {
                throw new ArgumentException($"Could not find point with id: {pointOfInterestId}");
            }

            if (string.IsNullOrWhiteSpace(point.PointOfInterestKey))
            {
                _logger.LogWarning("Point {PointOfInterestId} has no PointOfInterestKey, skipping SOLR indexing.", pointOfInterestId);
                return;
            }

            // Index the newest row for the key rather than the row the message names. Replacing a photo
            // inserts a new row under the same key, so indexing the named row would let two messages
            // arriving out of order leave stale data behind.
            List<GetPointOfInterestResponse> rows = await _context.GetPointsOfInterestByKeyAsync(point.PointOfInterestKey, cancellationToken);

            if (rows.Count == 0)
            {
                _logger.LogWarning("No read-model rows for key {PointOfInterestKey}, skipping SOLR indexing.", point.PointOfInterestKey);
                return;
            }

            await IndexBatchAsync(rows, cancellationToken);
        }

        public async Task IndexBatchAsync(IEnumerable<GetPointOfInterestResponse> rows, CancellationToken cancellationToken)
        {
            List<Dictionary<string, object>> documents = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.PointOfInterestKey))
                .GroupBy(row => row.PointOfInterestKey)
                .Select(group => ToDocument(group.Key, group))
                .ToList();

            if (documents.Count == 0) return;

            await _client.UpdateAsync(documents, cancellationToken);
            _logger.LogInformation("Indexed {DocumentCount} document(s) into SOLR collection {Collection}.", documents.Count, _client.Config.Collection);
        }

        public async Task PurgeAsync(CancellationToken cancellationToken)
        {
            var payload = new Dictionary<string, object>
            {
                ["delete"] = new Dictionary<string, object> { ["query"] = "*:*" }
            };

            await _client.UpdateAsync(payload, cancellationToken);
            _logger.LogInformation("Purged SOLR collection {Collection}.", _client.Config.Collection);
        }

        public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
        {
            HashSet<string> existing = await _client.GetSchemaFieldNamesAsync(cancellationToken);
            List<SolrField> missing = Fields.Where(field => !existing.Contains(field.Name)).ToList();

            if (missing.Count == 0)
            {
                _logger.LogInformation("SOLR schema for {Collection} is already up to date.", _client.Config.Collection);
                return;
            }

            foreach (SolrField field in missing)
            {
                var payload = new Dictionary<string, object>
                {
                    ["add-field"] = new Dictionary<string, object>
                    {
                        ["name"] = field.Name,
                        ["type"] = field.Type,
                        ["stored"] = true,
                        ["indexed"] = true,
                        ["multiValued"] = field.MultiValued,
                        ["required"] = false
                    }
                };

                try
                {
                    await _client.PostSchemaAsync(payload, cancellationToken);
                    _logger.LogInformation("Added SOLR field {Field} ({Type}).", field.Name, field.Type);
                }
                catch (FlurlHttpException ex)
                {
                    // A field that already exists is reported as a 400. Another instance racing us to the
                    // same schema is the expected cause, so log it and carry on rather than failing startup.
                    string body = await ex.GetResponseStringAsync();
                    _logger.LogWarning("Could not add SOLR field {Field}: {Response}", field.Name, body);
                }
            }
        }

        private static Dictionary<string, object> ToDocument(string pointOfInterestKey, IEnumerable<GetPointOfInterestResponse> rowsForKey)
        {
            List<GetPointOfInterestResponse> rows = rowsForKey.ToList();

            // Both indexing paths must agree on which row wins, or a rebuild would silently disagree with
            // the incremental path. spGetPointOfInterest() already applies this rule; applying it here too
            // means a batch straight from that procedure and a single incremental index converge.
            int latestPointOfInterestId = rows
                .OrderByDescending(row => row.DateCreated)
                .ThenByDescending(row => row.PointOfInterestId)
                .First()
                .PointOfInterestId;

            rows = rows.Where(row => row.PointOfInterestId == latestPointOfInterestId).ToList();
            GetPointOfInterestResponse first = rows[0];

            List<GetPointOfInterestResponse> tagged = rows
                .Where(row => row.TagId is not null && !string.IsNullOrWhiteSpace(row.TagName))
                .GroupBy(row => row.TagId.Value)
                .Select(group => group.First())
                .OrderBy(row => row.TagId)
                .ToList();

            var document = new Dictionary<string, object>
            {
                ["id"] = pointOfInterestKey,
                ["poi_id"] = first.PointOfInterestId,
                ["formatted_address"] = first.FormattedAddress ?? string.Empty,
                ["description"] = first.Description ?? string.Empty,
                ["date_created"] = FormatDate(first.DateCreated),
                ["latitude"] = first.Latitude,
                ["longitude"] = first.Longitude,
                ["container"] = first.Container ?? string.Empty,
                ["original_file_name"] = first.OriginalFileName ?? string.Empty,
                ["generated_blob_name"] = first.GeneratedBlobName ?? string.Empty,
                ["image_resized"] = first.ImageResized
            };

            // An add replaces the whole document, so a field left out here is genuinely absent afterwards.
            // Omitting nulls avoids sending a null SOLR would reject.
            if (first.DateTaken.HasValue) document["date_taken"] = FormatDate(first.DateTaken.Value);
            if (first.CorrelationId.HasValue) document["correlation_id"] = first.CorrelationId.Value.ToString();

            if (tagged.Count > 0)
            {
                // tags, tag_exact and tag_ids are written in the same order so the search side can pair a
                // tag name back up with the id it had in PostgreSQL.
                document["tags"] = tagged.Select(row => row.TagName).ToList();
                document["tag_exact"] = tagged.Select(row => row.TagName).ToList();
                document["tag_ids"] = tagged.Select(row => row.TagId.Value).ToList();
            }

            return document;
        }

        private static string FormatDate(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();

            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        private sealed record SolrField(string Name, string Type, bool MultiValued);
    }
}
