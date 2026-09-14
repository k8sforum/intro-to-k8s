using Microsoft.AspNetCore.Mvc;
using OpenFeature;
using System.Diagnostics.CodeAnalysis;
using mytravels.api.Extensions;
using mytravels.contract.CustomException;
using mytravels.contract.Dtos;
using mytravels.contract.Interfaces;
using mytravels.contract.Responses;

namespace mytravels.api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ExcludeFromCodeCoverage]
    public class PointOfInterestController : ControllerBase
    {
        private const int DefaultRows = 100;

        private readonly IPointOfInterestService _service;
        private readonly FeatureClient _featureClient;

        public PointOfInterestController
        (
            IPointOfInterestService service,
            FeatureClient featureClient
        )
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _featureClient = featureClient ?? throw new ArgumentNullException(nameof(featureClient));
        }

        /// <summary>
        /// Lists points of interest from SOLR. A point only appears once the messaging worker has consumed its
        /// index-solr message, and the list is capped at <paramref name="rows"/> with no paging loop behind it.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<PointOfInterestDto>), 200)]
        public async Task<IActionResult> GetMetadatasAsync(
            [FromQuery] int rows,
            [FromQuery] int start,
            CancellationToken cancellationToken)
        {
            SolrSearchQuery query = new()
            {
                Rows = rows <= 0 ? DefaultRows : rows,
                Start = start < 0 ? 0 : start
            };

            List<GetPointOfInterestResponse> response = await _service.GetAsync(query, cancellationToken);
            List<PointOfInterestDto> dtos = response.ToDto();
            return Ok(dtos);
        }

        /// <summary>
        /// Searches points of interest through SOLR across the formatted address, tags and AI-generated description.
        /// </summary>
        [HttpGet("search")]
        [ProducesResponseType(typeof(List<PointOfInterestDto>), 200)]
        public async Task<IActionResult> SearchAsync(
            [FromQuery] string term,
            [FromQuery] int rows,
            [FromQuery] int start,
            CancellationToken cancellationToken)
        {
            bool searchEnabled = await _featureClient.GetBooleanValueAsync("enable-poi-search", true);
            if (!searchEnabled)
            {
                return NotFound();
            }

            SolrSearchQuery query = new()
            {
                Term = term,
                Rows = rows <= 0 ? DefaultRows : rows,
                Start = start < 0 ? 0 : start
            };

            List<GetPointOfInterestResponse> response = await _service.SearchAsync(query, cancellationToken);
            List<PointOfInterestDto> dtos = response.ToDto();
            return Ok(dtos);
        }

        /// <summary>
        /// Queues a full rebuild of the SOLR index from PostgreSQL. The rebuild runs in the messaging worker;
        /// the returned correlation id can be followed through the traceability endpoints.
        /// </summary>
        [HttpPost("reindex")]
        [ProducesResponseType(typeof(SolrReindexResponseDto), 202)]
        public async Task<IActionResult> ReindexAsync([FromQuery] bool? purgeFirst, CancellationToken cancellationToken)
        {
            Guid correlationId = await _service.RequestReindexAsync(purgeFirst ?? true, cancellationToken);
            return Accepted(new SolrReindexResponseDto { CorrelationId = correlationId });
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(string), 200)]
        public async Task<IActionResult> GetImageAsync([FromRoute] int id, [FromQuery] bool resizedImage, CancellationToken cancellationToken)
        {
            string base64 = await _service.GetImageAsync(id, cancellationToken);
            return Ok(base64);
        }

        [HttpPut]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(SaveEntityResponseDto), 200)]
        public async Task<IActionResult> UpdateImageAsync(IFormFile image, [FromQuery] string pointOfInterestKey, CancellationToken cancellationToken)
        {
            if (image is null) throw new RequiredParameterNotFoundException(nameof(image));
            if (string.IsNullOrEmpty(pointOfInterestKey)) throw new RequiredParameterNotFoundException(nameof(pointOfInterestKey));
            int id = await _service.UpdatePointOfInterestAsync(image, pointOfInterestKey, cancellationToken);
            return Ok(new SaveEntityResponseDto { Id = id });
        }

        [HttpPost("image")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(SaveEntityResponseDto), 200)]
        public async Task<IActionResult> UploadImageAsync(IFormFile image, CancellationToken cancellationToken)
        {
            if (image is null) throw new RequiredParameterNotFoundException(nameof(image));
            int id = await _service.SaveFileAsPointOfInsterestAsync(image, cancellationToken);
            return Ok(new SaveEntityResponseDto { Id = id });
        }

        /// <summary>
        /// Uploads an image that carries no GPS metadata, using the coordinates supplied by the caller instead.
        /// </summary>
        [HttpPost("image/coordinates")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(SaveEntityResponseDto), 200)]
        public async Task<IActionResult> UploadImageWithCoordinatesAsync(IFormFile image, [FromForm] SaveCoordinatesDto coordinates, CancellationToken cancellationToken)
        {
            if (image is null) throw new RequiredParameterNotFoundException(nameof(image));

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            int id = await _service.SaveFileAsPointOfInsterestAsync(image, coordinates, cancellationToken);
            return Ok(new SaveEntityResponseDto { Id = id });
        }
    }
}
