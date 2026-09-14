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
    public class TraceabilityController : ControllerBase
    {
        private readonly ITraceabilityService _service;
        private readonly FeatureClient _featureClient;

        public TraceabilityController
        (
            ITraceabilityService service,
            FeatureClient featureClient
        )
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _featureClient = featureClient ?? throw new ArgumentNullException(nameof(featureClient));
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<CorrelationSummaryDto>), 200)]
        public async Task<IActionResult> GetCorrelationSummariesAsync([FromQuery] int page, [FromQuery] int pageSize, CancellationToken cancellationToken)
        {
            bool tracingEnabled = await _featureClient.GetBooleanValueAsync("enable-message-tracing", true);
            if (!tracingEnabled)
            {
                return NotFound();
            }

            List<CorrelationSummaryDto> dtos = await _service.GetCorrelationSummariesAsync(page <= 0 ? 1 : page, pageSize <= 0 ? 25 : pageSize, cancellationToken);
            return Ok(dtos);
        }

        [HttpGet("{correlationId:guid}")]
        [ProducesResponseType(typeof(List<MessageAuditLogDto>), 200)]
        public async Task<IActionResult> GetEventsAsync([FromRoute] Guid correlationId, CancellationToken cancellationToken)
        {
            bool tracingEnabled = await _featureClient.GetBooleanValueAsync("enable-message-tracing", true);
            if (!tracingEnabled)
            {
                return NotFound();
            }

            List<MessageAuditLogDto> dtos = await _service.GetEventsAsync(correlationId, cancellationToken);
            return Ok(dtos);
        }
    }
}
