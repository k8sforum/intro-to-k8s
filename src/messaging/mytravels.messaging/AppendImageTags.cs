using Microsoft.EntityFrameworkCore;
using OpenFeature;
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
    private readonly FeatureClient _featureClient;
    private static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

    public AppendImageTags
        (
            ILogger<AppendImageTags> logger,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory,
            FeatureClient featureClient)
        : base(logger, configuration, ExchangeNames.AppendImageTags, ExchangeNames.AppendImageTags, ExchangeNames.AppendImageTagsFailed, serviceScopeFactory)
    {
        _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
        _featureClient = featureClient ?? throw new ArgumentNullException(nameof(featureClient));
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
            IMessagePublisher publisher = scope.ServiceProvider.GetRequiredService<IMessagePublisher>();

            PointOfInterest point = await context.PointOfInterests.FirstOrDefaultAsync(x => x.Id == obj.PointOfInterestId, cancellationToken);

            if (point is null)
            {
                throw new ArgumentException($"Could not find point with id: {obj.PointOfInterestId}");
            }

            if (string.IsNullOrWhiteSpace(point.GeneratedBlobName))
            {
                return;
            }

            bool imageDescriptionEnabled = await _featureClient.GetBooleanValueAsync("enable-image-description", true);
            if (imageDescriptionEnabled)
            {
                // ResizeImage runs immediately before this subscriber in the chain and always leaves a
                // resized copy in ResizedImagesContainer under the same blob name (either freshly written,
                // or already there from a prior run). Use that instead of the full-resolution original —
                // it's a fraction of the pixel count, which keeps memory use and the Anthropic request size
                // bounded regardless of how large the uploaded photo was.
                string base64 = await objectStorageService.GetBase64Async(BucketNames.ResizedImagesContainer, point.GeneratedBlobName, cancellationToken);
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

            // Description and tags are two of the three fields SOLR ranks on, so reindex now that they exist.
            // The inbound CorrelationId is reused so the reindex shows up on the same trace as the upload.
            await publisher.PublishAsync(ExchangeNames.IndexSolr, new PointOfInterestMessage { CorrelationId = obj.CorrelationId, PointOfInterestId = point.Id }, cancellationToken);
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
