using Microsoft.EntityFrameworkCore;
using mytravels.common.Services;
using mytravels.contract.Entities;
using mytravels.contract.Interfaces;
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

    protected override async Task DoWorkAsync()
    {
        try
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            ICoreDbContext context = scope.ServiceProvider.GetRequiredService<ICoreDbContext>();
            IGoogleMapsService googleMapsService = scope.ServiceProvider.GetRequiredService<IGoogleMapsService>();

            List<PointOfInterest> points = await context.GetPointsOfInterestAsync(default);

            points = points.Where(x => x.DateCreated > DateTime.UtcNow.AddDays(-2) && x.FormattedAddress == "")
                           .ToList();

            foreach (PointOfInterest point in points)
            {
                point.FormattedAddress = await googleMapsService.GetAddressAsync(point.Latitude, point.Longitude, default);
                var entry = context.Entry(point);
                entry.State = EntityState.Unchanged;
                entry.Property(nameof(point.FormattedAddress)).IsModified = true;
                await context.SaveChangesAsync(default);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error: {ex.Message}");
            throw;
        }
    }
}