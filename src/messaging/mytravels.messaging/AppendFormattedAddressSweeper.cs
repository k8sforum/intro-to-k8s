using Microsoft.EntityFrameworkCore;
using mytravels.common.Services;
using mytravels.contract.Constants;
using mytravels.contract.Entities;
using mytravels.contract.Interfaces;
using mytravels.contract.Messages;
using mytravels.domain;

namespace mytravels.functions;

public class AppendFormattedAddressSweeper : CronJobBase
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public AppendFormattedAddressSweeper
        (
            ILogger<CronJobBase> logger,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory)
        : base(logger, TimeSpan.FromMinutes(30))
    {
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
    }

    protected override async Task DoWorkAsync(CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            ICoreDbContext context = scope.ServiceProvider.GetRequiredService<ICoreDbContext>();
            IMapsService mapsService = scope.ServiceProvider.GetRequiredService<IMapsService>();

            IMessagePublisher publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();

            List<PointOfInterest> points = await context.GetPointsMissingAddressAsync(DateTime.UtcNow.AddDays(-2), cancellationToken);

            foreach (PointOfInterest point in points)
            {
                try
                {
                    point.FormattedAddress = await mapsService.GetAddressAsync(point.Latitude, point.Longitude, cancellationToken);
                    var entry = context.Entry(point);
                    entry.State = EntityState.Unchanged;
                    entry.Property(nameof(point.FormattedAddress)).IsModified = true;
                    await context.SaveChangesAsync(cancellationToken);

                    // The address is a SOLR-ranked field, so a swept point must be reindexed like a consumed one.
                    await publisher.PublishAsync(ExchangeNames.IndexSolr, new PointOfInterestMessage { CorrelationId = point.CorrelationId ?? Guid.NewGuid(), PointOfInterestId = point.Id }, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sweeper failed to geocode POI {PointOfInterestId}, correlation {CorrelationId}",
                        point.Id,
                        point.CorrelationId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error: {ex.Message}");
            throw;
        }
    }
}