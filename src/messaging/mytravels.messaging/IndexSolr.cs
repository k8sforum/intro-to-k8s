using mytravels.common.Services;
using mytravels.contract.Constants;
using mytravels.contract.Interfaces;
using mytravels.contract.Messages;

namespace mytravels.functions;

public class IndexSolr : MessageSubscriberBase<PointOfInterestMessage>
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public IndexSolr
        (
            ILogger<IndexSolr> logger,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory)
        : base(logger, configuration, ExchangeNames.IndexSolr, ExchangeNames.IndexSolr, ExchangeNames.IndexSolrFailed, serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
    }

    protected override async Task ProcessMessageAsync(PointOfInterestMessage obj, CancellationToken cancellationToken)
    {
        if (obj is null) return;
        try
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            ISolrIndexService indexService = scope.ServiceProvider.GetRequiredService<ISolrIndexService>();

            await indexService.IndexAsync(obj.PointOfInterestId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing IndexSolr message for POI {PointOfInterestId}, correlation {CorrelationId}",
                obj.PointOfInterestId,
                obj.CorrelationId);
            throw;
        }
    }
}
