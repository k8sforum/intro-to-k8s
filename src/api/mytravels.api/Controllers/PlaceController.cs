using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.CodeAnalysis;
using mytravels.contract.CustomException;
using mytravels.contract.Dtos;
using mytravels.contract.Interfaces;

namespace mytravels.api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ExcludeFromCodeCoverage]
    public class PlaceController : ControllerBase
    {
        private const int DefaultLimit = 5;

        private readonly IMapsService _mapsService;

        public PlaceController
        (
            IMapsService mapsService
        )
        {
            _mapsService = mapsService ?? throw new ArgumentNullException(nameof(mapsService));
        }

        /// <summary>
        /// Resolves a free text location name into candidate places, so an image without GPS metadata can be placed on the map.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(List<PlaceDto>), 200)]
        public async Task<IActionResult> SearchAsync([FromQuery] string query, [FromQuery] int limit, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(query)) throw new RequiredParameterNotFoundException(nameof(query));

            List<PlaceDto> places = await _mapsService.SearchPlacesAsync(query, limit > 0 ? limit : DefaultLimit, cancellationToken);
            return Ok(places);
        }
    }
}
