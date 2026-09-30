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
        private readonly IImageDescriptionService _imageDescriptionService;
        private readonly ISolrSearchService _solrSearchService;

        public PointOfInterestService
        (
            IObjectStorageService service,
            ICoreDbContext context,
            IMessagePublisher publisher,
            IGeoService geoService,
            IImageDescriptionService imageDescriptionService,
            ISolrSearchService solrSearchService
        )
        {
            _objectStorageService = service ?? throw new ArgumentNullException(nameof(service));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            _geoService = geoService ?? throw new ArgumentNullException(nameof(geoService));
            _imageDescriptionService = imageDescriptionService ?? throw new ArgumentNullException(nameof(imageDescriptionService));
            _solrSearchService = solrSearchService ?? throw new ArgumentNullException(nameof(solrSearchService));
        }

        /// <summary>
        /// Serves the POI list from SOLR rather than PostgreSQL, so the map and search read the same index.
        /// A blank term makes SolrSearchService issue q=*:*; PostgreSQL is only read by the reindex rebuild now.
        /// </summary>
        public async Task<List<GetPointOfInterestResponse>> GetAsync(SolrSearchQuery query, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(query);

            SolrSearchQuery listQuery = new()
            {
                Term = null,
                Rows = query.Rows,
                Start = query.Start
            };

            return await _solrSearchService.SearchAsync(listQuery, cancellationToken);
        }

        public async Task<List<GetPointOfInterestResponse>> SearchAsync(SolrSearchQuery query, CancellationToken cancellationToken)
            => await _solrSearchService.SearchAsync(query, cancellationToken);

        public async Task<Guid> RequestReindexAsync(bool purgeFirst, CancellationToken cancellationToken)
        {
            Guid correlationId = Guid.NewGuid();
            await _publisher.PublishAsync(ExchangeNames.ReindexSolr, new SolrReindexMessage { CorrelationId = correlationId, PurgeFirst = purgeFirst }, cancellationToken);
            return correlationId;
        }

        public async Task<int> SaveFileAsPointOfInsterestAsync(IFormFile file, CancellationToken cancellationToken)
        {
            // Validate before touching storage, so a rejected upload leaves no orphaned blob behind.
            ImageMetadata metadata = ReadImageMetadata(file);
            if (metadata.GeoLocation is not { } geoLocation)
                throw new InvalidImageException("Image is not geocoded");

            string objectName = await _objectStorageService.SaveObjectAsync(file, BucketNames.NewUploadedImagesContainer, cancellationToken);

            SaveCoordinatesDto coordinates = new()
            {
                Latitude = geoLocation.Latitude,
                Longitude = geoLocation.Longitude,
                FormattedAddress = string.Empty
            };

            return await CreatePointOfInterestAsync(file, objectName, coordinates, metadata.DateTaken, cancellationToken);
        }

        public async Task<int> SaveFileAsPointOfInsterestAsync(IFormFile file, SaveCoordinatesDto coordinates, CancellationToken cancellationToken)
        {
            // The caller supplies the location, so the image is only read for its capture date and may carry no metadata at all.
            ImageMetadata metadata = TryReadImageMetadata(file);

            string objectName = await _objectStorageService.SaveObjectAsync(file, BucketNames.NewUploadedImagesContainer, cancellationToken);

            return await CreatePointOfInterestAsync(file, objectName, coordinates, metadata?.DateTaken, cancellationToken);
        }

        public async Task<int> UpdatePointOfInterestAsync(IFormFile file, string pointOfInterestKey, CancellationToken cancellationToken)
        {
            contract.Entities.PointOfInterest point = await _context.GetLatestPointOfInterestByKeyAsync(pointOfInterestKey, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(point));

            string objectName = await _objectStorageService.SaveObjectAsync(file, BucketNames.NewUploadedImagesContainer, cancellationToken);

            // Reuse the existing CorrelationId or mint one, before the row is re-inserted so it is saved once.
            if (point.CorrelationId is null || point.CorrelationId == Guid.Empty)
            {
                point.CorrelationId = Guid.NewGuid();
            }

            await _context.AddImageToPointOfInterestAsync(objectName, point, cancellationToken);

            await _publisher.PublishAsync(ExchangeNames.ResizeImage, new PointOfInterestMessage { CorrelationId = point.CorrelationId.Value, PointOfInterestId = point.Id }, cancellationToken);
            return point.Id;
        }

        public async Task<string> GetImageAsync(int id, bool resizedImage, CancellationToken cancellationToken)
        {
            contract.Entities.PointOfInterest point = await _context.PointOfInterests
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new DataNotFoundException($"Point of interest with id '{id}' was not found");

            if (string.IsNullOrWhiteSpace(point.GeneratedBlobName)) return string.Empty;

            // The resized copy only exists once ResizeImage has run, so fall back to the original until then.
            string bucket = resizedImage && point.ImageResized
                ? BucketNames.ResizedImagesContainer
                : BucketNames.NewUploadedImagesContainer;

            return await _objectStorageService.GetBase64Async(bucket, point.GeneratedBlobName, cancellationToken);
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

            // Mint and set CorrelationId before persisting
            Guid correlationId = Guid.NewGuid();
            point.CorrelationId = correlationId;

            int id = await _context.CreatePointOfInterestAsync(point, cancellationToken);

            await _publisher.PublishAsync(ExchangeNames.AppendFormattedAddress, new PointOfInterestMessage { CorrelationId = correlationId, PointOfInterestId = id }, cancellationToken);

            // append-image-tags is chained from ResizeImage rather than published here, so the description and
            // tags are generated exactly once per image and the index-solr message that carries them is only
            // published after the description service has succeeded.
            await _publisher.PublishAsync(ExchangeNames.ResizeImage, new PointOfInterestMessage { CorrelationId = correlationId, PointOfInterestId = id }, cancellationToken);

            // Index immediately so the point is findable by date and coordinates straight away. The address,
            // description and tags all arrive asynchronously, so each enrichment subscriber republishes here.
            await _publisher.PublishAsync(ExchangeNames.IndexSolr, new PointOfInterestMessage { CorrelationId = correlationId, PointOfInterestId = id }, cancellationToken);

            return id;
        }

        private ImageMetadata ReadImageMetadata(IFormFile file)
        {
            using Stream stream = file.OpenReadStream();
            return _geoService.ExtractImageMetadata(stream);
        }

        private ImageMetadata TryReadImageMetadata(IFormFile file)
        {
            try
            {
                return ReadImageMetadata(file);
            }
            catch (ImageProcessingException)
            {
                return null;
            }
        }
    }
}
