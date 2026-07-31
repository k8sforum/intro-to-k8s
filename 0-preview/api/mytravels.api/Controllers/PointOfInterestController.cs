using Microsoft.AspNetCore.Mvc;
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
        private readonly IPointOfInterestService _service;

        public PointOfInterestController
        (
            IPointOfInterestService service
        )
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<PointOfInterestDto>), 200)]
        public async Task<IActionResult> GetMetadatasAsync(CancellationToken cancellationToken)
        {
            List<GetPointOfInterestResponse> response = await _service.GetAsync(cancellationToken);
            List<PointOfInterestDto> dtos = response.ToDto();
            return Ok(dtos);
        }

        [HttpGet("filter")]
        [ProducesResponseType(typeof(List<PointOfInterestDto>), 200)]
        public async Task<IActionResult> GetMetadatasAsync([FromQuery] string filterString, CancellationToken cancellationToken)
        {
            List<GetPointOfInterestResponse> response = await _service.GetAsync(filterString, cancellationToken);
            List<PointOfInterestDto> dtos = response.ToDto();
            return Ok(dtos);
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(string), 200)]
        public async Task<IActionResult> GetImageAsync([FromRoute] int id, [FromQuery] bool resizedImage, CancellationToken cancellationToken)
        {
            string base64 = await _service.GetImageAsync(id, cancellationToken);
            return Ok(base64);
        }

        [HttpPut("status")]
        public async Task<IActionResult> UpdateStatusAsync([FromBody] UpdatePointOfInterestStatusDto dto, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            int id = await _service.UpdateStatusAsync(dto.PointOfInterestKey, dto.StatusId, cancellationToken);
            return Ok(new SaveEntityResponseDto
            {
                Id = id
            });
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
    }
}
