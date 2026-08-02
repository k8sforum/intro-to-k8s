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
        : base(logger, configuration, ExchangeNames.AppendFormattedAddress, ExchangeNames.AppendFormattedAddress)
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing AppendFormattedAddress message");
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }
}
