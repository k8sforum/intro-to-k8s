using Microsoft.AspNetCore.Mvc;
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

        public TraceabilityController
        (
            ITraceabilityService service
        )
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<CorrelationSummaryDto>), 200)]
        public async Task<IActionResult> GetCorrelationSummariesAsync([FromQuery] int page, [FromQuery] int pageSize, CancellationToken cancellationToken)
        {
            List<CorrelationSummaryDto> dtos = await _service.GetCorrelationSummariesAsync(page <= 0 ? 1 : page, pageSize <= 0 ? 25 : pageSize, cancellationToken);
            return Ok(dtos);
        }

        [HttpGet("{correlationId:guid}")]
        [ProducesResponseType(typeof(List<MessageAuditLogDto>), 200)]
        public async Task<IActionResult> GetEventsAsync([FromRoute] Guid correlationId, CancellationToken cancellationToken)
        {
            List<MessageAuditLogDto> dtos = await _service.GetEventsAsync(correlationId, cancellationToken);
            return Ok(dtos);
        }
    }
}
