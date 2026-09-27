using Microsoft.AspNetCore.Mvc;
using OpenFeature;
using System.Diagnostics.CodeAnalysis;
using mytravels.contract.Dtos;
using mytravels.contract.Interfaces;

namespace mytravels.api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [ExcludeFromCodeCoverage]
    public class FailedMessagesController : ControllerBase
    {
        private readonly IFailedMessageService _service;
        private readonly FeatureClient _featureClient;

        public FailedMessagesController
        (
            IFailedMessageService service,
            FeatureClient featureClient
        )
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _featureClient = featureClient ?? throw new ArgumentNullException(nameof(featureClient));
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<FailedMessageDto>), 200)]
        public async Task<IActionResult> GetFailedMessagesAsync(CancellationToken cancellationToken)
        {
            bool adminEnabled = await _featureClient.GetBooleanValueAsync("enable-failed-message-admin", true);
            if (!adminEnabled)
            {
                return NotFound();
            }

            List<FailedMessageDto> dtos = await _service.GetFailedMessagesAsync(cancellationToken);
            return Ok(dtos);
        }

        [HttpPost("{id:int}/retry")]
        [ProducesResponseType(204)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> RetryAsync([FromRoute] int id, CancellationToken cancellationToken)
        {
            bool adminEnabled = await _featureClient.GetBooleanValueAsync("enable-failed-message-admin", true);
            if (!adminEnabled)
            {
                return NotFound();
            }

            bool retried = await _service.RetryAsync(id, cancellationToken);
            return retried ? NoContent() : NotFound();
        }
    }
}
