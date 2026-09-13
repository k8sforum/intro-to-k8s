using Microsoft.EntityFrameworkCore;
using mytravels.common.Services;
using mytravels.contract.Constants;
using mytravels.contract.Entities;
using mytravels.contract.Interfaces;
using mytravels.contract.Messages;
using mytravels.domain;

namespace mytravels.functions;

public class AppendFormattedAddress : MessageSubscriberBase<PointOfInterestMessage>
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

    public AppendFormattedAddress
        (
            ILogger<AppendFormattedAddress> logger,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory)
        : base(logger, configuration, ExchangeNames.AppendFormattedAddress, ExchangeNames.AppendFormattedAddress, ExchangeNames.AppendFormattedAddressFailed, serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
    }

    protected override async Task ProcessMessageAsync(PointOfInterestMessage obj, CancellationToken cancellationToken)
    {
        if (obj is null) return;
        try
        {
            await semaphore.WaitAsync();
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            ICoreDbContext context = scope.ServiceProvider.GetRequiredService<ICoreDbContext>();
            IMapsService mapsService = scope.ServiceProvider.GetRequiredService<IMapsService>();
            IMessagePublisher publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();

            PointOfInterest point = await context.PointOfInterests.FirstOrDefaultAsync(x => x.Id == obj.PointOfInterestId, cancellationToken);

            if (point is null)
            {
                throw new ArgumentException($"Could not find point with id: {obj.PointOfInterestId}");
            }

            if (string.IsNullOrEmpty(point.FormattedAddress?.Trim()))
            {
                point.FormattedAddress = await mapsService.GetAddressAsync(point.Latitude, point.Longitude, default);
                var entry = context.Entry(point);
                entry.State = EntityState.Unchanged;
                entry.Property(nameof(point.FormattedAddress)).IsModified = true;
                await context.SaveChangesAsync(default);
            }

            // The address is one of the fields SOLR ranks on, so reindex now that it exists. The inbound
            // CorrelationId is reused so the reindex shows up on the same trace as the upload.
            await publisher.PublishAsync(ExchangeNames.IndexSolr, new PointOfInterestMessage { CorrelationId = obj.CorrelationId, PointOfInterestId = point.Id }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing AppendFormattedAddress message for POI {PointOfInterestId}, correlation {CorrelationId}",
                obj.PointOfInterestId,
                obj.CorrelationId);
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }
}
