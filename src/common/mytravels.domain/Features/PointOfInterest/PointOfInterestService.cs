using MetadataExtractor;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using mytravels.contract.CustomException;
using mytravels.contract.Constants;
using mytravels.contract.Dtos;
using mytravels.contract.Interfaces;
using mytravels.contract.Messages;
using mytravels.contract.Responses;
using mytravels.domain.Extensions;

namespace mytravels.domain.Features.PointOfInterest
{
    public class PointOfInterestService : IPointOfInterestService
    {
        private readonly ICoreDbContext _context;
        private readonly IObjectStorageService _objectStorageService;
        private readonly IMessagePublisher _publisher;
        private readonly IGeoService _geoService;

        public PointOfInterestService
        (
            IObjectStorageService service,
            ICoreDbContext context,
            IMessagePublisher publisher,
            IGeoService geoService
        )
        {
            _objectStorageService = service ?? throw new ArgumentNullException(nameof(service));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _geoService = geoService ?? throw new ArgumentNullException(nameof(geoService));
        }

        public async Task<List<GetPointOfInterestResponse>> GetAsync(CancellationToken cancellationToken)
            => await _context.GetAllPointsOfInterestAsync(cancellationToken);

        public async Task<List<GetPointOfInterestResponse>> GetAsync(string tagName, CancellationToken cancellationToken)
            => await _context.GetPointsOfInterestByTagAsync(tagName, cancellationToken);

        public async Task<int> SaveFileAsPointOfInsterestAsync(IFormFile file, CancellationToken cancellationToken)
        {
            string objectName = await _objectStorageService.SaveObjectAsync(file, BucketNames.NewUploadedImagesContainer, cancellationToken);
            ImageMetadata metadata = await GetImageMetadataAsync(objectName, cancellationToken);

            if (metadata.GeoLocation.Latitude == 0 || metadata.GeoLocation.Longitude == 0)
                throw new InvalidOperationException("Image is not geocoded");

            SaveCoordinatesDto coordinates = new()
            {
                Latitude = metadata.GeoLocation.Latitude,
                Longitude = metadata.GeoLocation.Longitude,
                FormattedAddress = string.Empty
            };

            return await CreatePointOfInterestAsync(file, objectName, coordinates, metadata.DateTaken, cancellationToken);
        }

        public async Task<int> SaveFileAsPointOfInsterestAsync(IFormFile file, SaveCoordinatesDto coordinates, CancellationToken cancellationToken)
        {
            string objectName = await _objectStorageService.SaveObjectAsync(file, BucketNames.NewUploadedImagesContainer, cancellationToken);

            // The caller supplies the location, so the image is only read for its capture date and may carry no metadata at all.
            ImageMetadata metadata = await TryGetImageMetadataAsync(objectName, cancellationToken);

            return await CreatePointOfInterestAsync(file, objectName, coordinates, metadata?.DateTaken, cancellationToken);
        }

        public async Task<int> UpdatePointOfInterestAsync(IFormFile file, string pointOfInterestKey, CancellationToken cancellationToken)
        {
            string objectName = await _objectStorageService.SaveObjectAsync(file, BucketNames.NewUploadedImagesContainer, cancellationToken);
            List<contract.Entities.PointOfInterest> points = await _context.GetPointsOfInterestAsync(cancellationToken);
            contract.Entities.PointOfInterest point = points.Where(x => x.PointOfInterestKey == pointOfInterestKey)
                                                   .OrderByDescending(x => x.DateCreated)
                                                   .FirstOrDefault() ?? throw new EntityNotFoundException(nameof(point));

            await _context.AddImageToPointOfInterestAsync(objectName, point, cancellationToken);
            await _publisher.PublishAsync(ExchangeNames.ResizeImage, new PointOfInterestMessage { PointOfInterestId = point.Id }, CancellationToken.None);
            return point.Id;
        }

        public async Task<string> GetImageAsync(int id, CancellationToken cancellationToken)
        {
            contract.Entities.PointOfInterest point = await _context.PointOfInterests
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new DataNotFoundException($"Point of interest with id '{id}' was not found");

            if (string.IsNullOrWhiteSpace(point.GeneratedBlobName)) return string.Empty;

            return await _objectStorageService.GetBase64Async(BucketNames.ResizedImagesContainer, point.GeneratedBlobName, cancellationToken);
        }

        private async Task<int> CreatePointOfInterestAsync(IFormFile file, string objectName, SaveCoordinatesDto coordinates, DateTime? dateTaken, CancellationToken cancellationToken)
        {
            CreatePointOfInterestDto dto = new()
            {
                OriginalFileName = file.FileName,
                BlobName = objectName,
                FormattedAddress = coordinates.FormattedAddress ?? string.Empty,
                Latitude = coordinates.Latitude,
                Longitude = coordinates.Longitude,
                DateTaken = dateTaken
            };

            contract.Entities.PointOfInterest point = dto.ToEntity();
            int id = await _context.CreatePointOfInterestAsync(point, cancellationToken);

            await _publisher.PublishAsync(ExchangeNames.AppendFormattedAddress, new PointOfInterestMessage { CorrelationId = Guid.NewGuid(), PointOfInterestId = id }, cancellationToken);
            await _publisher.PublishAsync(ExchangeNames.ResizeImage, new PointOfInterestMessage { PointOfInterestId = point.Id }, cancellationToken);

            return id;
        }

        private async Task<ImageMetadata> GetImageMetadataAsync(string generatedObjectName, CancellationToken cancellationToken)
        {
            Stream savedImage = await _objectStorageService.GetStreamAsync(BucketNames.NewUploadedImagesContainer, generatedObjectName, cancellationToken);
            return _geoService.ExtractImageMetadata(savedImage);
        }

        private async Task<ImageMetadata> TryGetImageMetadataAsync(string generatedObjectName, CancellationToken cancellationToken)
        {
            try
            {
                return await GetImageMetadataAsync(generatedObjectName, cancellationToken);
            }
            catch (ImageProcessingException)
            {
                return null;
            }
        }
    }
}
