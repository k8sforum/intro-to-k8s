using Microsoft.Extensions.Options;
using mytravels.common.Services;
using mytravels.contract.Config;
using mytravels.contract.Constants;
using mytravels.contract.Interfaces;
using mytravels.contract.Messages;
using mytravels.contract.Responses;
using mytravels.domain;

namespace mytravels.functions;

/// <summary>
/// Rebuilds the whole SOLR collection from PostgreSQL. This is the recovery path for a lost index, and it
/// must converge on the same document set the incremental IndexSolr path produces - both read the
/// latest-row-per-key read model and key documents on PointOfInterestKey.
/// </summary>
public class ReindexSolr : MessageSubscriberBase<SolrReindexMessage>
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly SolrConfig _solrConfig;

    public ReindexSolr
        (
            ILogger<ReindexSolr> logger,
            IConfiguration configuration,
            IOptions<SolrConfig> solrConfig,
            IServiceScopeFactory serviceScopeFactory)
        : base(logger, configuration, ExchangeNames.ReindexSolr, ExchangeNames.ReindexSolr, ExchangeNames.ReindexSolrFailed, serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        _solrConfig = solrConfig?.Value ?? throw new ArgumentNullException(nameof(solrConfig));
    }

    protected override async Task ProcessMessageAsync(SolrReindexMessage obj, CancellationToken cancellationToken)
    {
        if (obj is null) return;
        try
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            ICoreDbContext context = scope.ServiceProvider.GetRequiredService<ICoreDbContext>();
            ISolrIndexService indexService = scope.ServiceProvider.GetRequiredService<ISolrIndexService>();

            if (obj.PurgeFirst)
            {
                await indexService.PurgeAsync(cancellationToken);
            }

            // spGetPointOfInterest() already returns one row per tag for the newest row of each key, so the
            // rows can be handed straight to the indexer once they are grouped back up by key.
            List<GetPointOfInterestResponse> rows = await context.GetAllPointsOfInterestAsync(cancellationToken);

            List<IGrouping<string, GetPointOfInterestResponse>> groups = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.PointOfInterestKey))
                .GroupBy(row => row.PointOfInterestKey)
                .ToList();

            int batchSize = _solrConfig.BatchSize <= 0 ? 500 : _solrConfig.BatchSize;
            int indexed = 0;
            int batchNumber = 0;
            int batchCount = (int)Math.Ceiling(groups.Count / (double)batchSize);

            _logger.LogInformation("Reindex starting for correlation {CorrelationId}: {PointCount} point(s) in {BatchCount} batch(es) of {BatchSize}.",
                obj.CorrelationId, groups.Count, batchCount, batchSize);

            for (int offset = 0; offset < groups.Count; offset += batchSize)
            {
                List<GetPointOfInterestResponse> batch = groups.Skip(offset)
                                                               .Take(batchSize)
                                                               .SelectMany(group => group)
                                                               .ToList();

                await indexService.IndexBatchAsync(batch, cancellationToken);

                batchNumber++;
                indexed += Math.Min(batchSize, groups.Count - offset);

                // A rebuild is the one operation here with no natural per-item observability, so each batch reports.
                _logger.LogInformation("Reindex batch {BatchNumber}/{BatchCount} done for correlation {CorrelationId}: {Indexed}/{Total} point(s) indexed.",
                    batchNumber, batchCount, obj.CorrelationId, indexed, groups.Count);
            }

            _logger.LogInformation("Reindex complete for correlation {CorrelationId}: {Indexed} point(s) indexed.", obj.CorrelationId, indexed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ReindexSolr message for correlation {CorrelationId}", obj.CorrelationId);
            throw;
        }
    }
}
