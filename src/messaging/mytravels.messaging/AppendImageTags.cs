using Microsoft.EntityFrameworkCore;
using mytravels.common.Services;
using mytravels.contract.Constants;
using mytravels.contract.Dtos;
using mytravels.contract.Entities;
using mytravels.contract.Interfaces;
using mytravels.contract.Messages;
using mytravels.domain;

namespace mytravels.functions;

public class AppendImageTags : MessageSubscriberBase<PointOfInterestMessage>
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

    public AppendImageTags
        (
            ILogger<AppendImageTags> logger,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory)
        : base(logger, configuration, ExchangeNames.AppendImageTags, ExchangeNames.AppendImageTags, ExchangeNames.AppendImageTagsFailed, serviceScopeFactory)
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
            IObjectStorageService objectStorageService = scope.ServiceProvider.GetRequiredService<IObjectStorageService>();
            IImageDescriptionService imageDescriptionService = scope.ServiceProvider.GetRequiredService<IImageDescriptionService>();

            PointOfInterest point = await context.PointOfInterests.FirstOrDefaultAsync(x => x.Id == obj.PointOfInterestId, cancellationToken);

            if (point is null)
            {
                throw new ArgumentException($"Could not find point with id: {obj.PointOfInterestId}");
            }

            if (string.IsNullOrWhiteSpace(point.GeneratedBlobName))
            {
                return;
            }

            string base64 = await objectStorageService.GetBase64Async(BucketNames.NewUploadedImagesContainer, point.GeneratedBlobName, cancellationToken);
            ImageDescriptionDto result = await imageDescriptionService.DescribeAsync(base64, cancellationToken);

            point.Description = result.Description;
            var entry = context.Entry(point);
            entry.State = EntityState.Unchanged;
            entry.Property(nameof(point.Description)).IsModified = true;
            await context.SaveChangesAsync(cancellationToken);

            if (result.Tags.Count > 0)
            {
                await context.UpdatePointOfInterestTagsAsync(
                    new List<SavePointOfInterestDto>
                    {
                        new SavePointOfInterestDto { PointOfInterestId = point.Id, Tags = result.Tags }
                    },
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing AppendImageTags message for POI {PointOfInterestId}, correlation {CorrelationId}",
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
