using MetadataExtractor;
using Microsoft.AspNetCore.Http;
using mytravels.contract.CustomException;
using mytravels.contract.Constants;
using mytravels.contract.Dtos;
using mytravels.contract.Interfaces;
using mytravels.contract.Lookups;
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
            GeoLocation geolocation = await GetCoordinatesAsync(objectName, cancellationToken);

            CreatePointOfInterestDto dto = new()
            {
                OriginalFileName = file.FileName,
                BlobName = objectName,
                FormattedAddress = string.Empty,
                Latitude = geolocation.Latitude,
                Longitude = geolocation.Longitude,
                PointOfInterestTypeId = (int)PointOfInterestTypesEnum.Image
            };

            contract.Entities.PointOfInterest point = dto.ToEntity();
            int id = await _context.CreatePointOfInterestAsync(point, cancellationToken);

            await _publisher.PublishAsync(ExchangeNames.AppendFormattedAddress, new PointOfInterestMessage { CorrelationId = Guid.NewGuid(), PointOfInterestId = id }, cancellationToken);
            await _publisher.PublishAsync(ExchangeNames.ResizeImage, new PointOfInterestMessage { PointOfInterestId = point.Id }, cancellationToken);

            return id;
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

        public async Task<int> UpdateStatusAsync(string pointOfInterestKey, int pointOfInterestStatusId, CancellationToken cancellationToken)
        {
            List<contract.Entities.PointOfInterest> points = await _context.GetPointsOfInterestAsync(cancellationToken);
            contract.Entities.PointOfInterest point = points.Where(x => x.PointOfInterestKey == pointOfInterestKey)
                                                   .OrderByDescending(x => x.DateCreated)
                                                   .FirstOrDefault() ?? throw new EntityNotFoundException(nameof(point));

            await _context.UpdatePointOfInterestStatusAsync(pointOfInterestStatusId, point, cancellationToken);
            return point.Id;
        }

        private async Task<GeoLocation> GetCoordinatesAsync(string generatedObjectName, CancellationToken cancellationToken)
        {
            Stream savedImage = await _objectStorageService.GetStreamAsync(BucketNames.NewUploadedImagesContainer, generatedObjectName, cancellationToken);
            GeoLocation geolocation = _geoService.ExtractGeoLocation(savedImage);
            if (geolocation.Latitude == 0 || geolocation.Longitude == 0)
                throw new InvalidOperationException("Image is not geocoded");
            return geolocation;
        }
    }
}
