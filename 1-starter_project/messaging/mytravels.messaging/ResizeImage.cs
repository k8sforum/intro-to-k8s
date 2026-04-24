using ImageMagick;
using Microsoft.EntityFrameworkCore;
using mytravels.common.Services;
using mytravels.contract.Constants;
using mytravels.contract.Entities;
using mytravels.contract.Interfaces;
using mytravels.contract.Messages;
using mytravels.domain;

namespace mytravels.functions;

public class ResizeImage : MessageSubscriberBase<PointOfInterestMessage>
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

    public ResizeImage
        (
            ILogger<ResizeImage> logger,
            IConfiguration configuration,
            IServiceScopeFactory serviceScopeFactory)
        : base(logger, configuration, ExchangeNames.ResizeImage, ExchangeNames.ResizeImage)
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


            PointOfInterest point = await context.PointOfInterests.FirstOrDefaultAsync(x => x.Id == obj.PointOfInterestId, cancellationToken);

            if (point is null)
            {
                throw new ArgumentException($"Could not find point with id: {obj.PointOfInterestId}");
            }

            if (point.ImageResized)
            {
                return;
            }
 
            Stream stream = await objectStorageService.GetStreamAsync(BucketNames.NewUploadedImagesContainer, point.GeneratedBlobName, CancellationToken.None);
            using var image = new MagickImage(stream);
            uint newWidth = (uint)(image.Width * 0.1);
            uint newHeight = (uint)(image.Height * 0.1);

            image.Resize(newWidth, newHeight);
            using var resizedStream = new MemoryStream();
            await image.WriteAsync(resizedStream, MagickFormat.Jpeg);
            resizedStream.Position = 0;
            await objectStorageService.SaveObjectAsync(BucketNames.ResizedImagesContainer, point.GeneratedBlobName, resizedStream, CancellationToken.None);
            point.ImageResized = true;
            var entry = context.Entry(point);
            entry.State = EntityState.Unchanged;
            entry.Property(nameof(point.ImageResized)).IsModified = true;
            await context.SaveChangesAsync(CancellationToken.None);

        }
        finally
        {
            semaphore.Release();
        }
    }
}
