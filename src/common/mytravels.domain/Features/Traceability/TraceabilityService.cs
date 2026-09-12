using mytravels.contract.Dtos;
using mytravels.contract.Interfaces;

namespace mytravels.domain.Features.Traceability;

public class TraceabilityService : ITraceabilityService
{
    private readonly ICoreDbContext _context;

    public TraceabilityService(ICoreDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<CorrelationSummaryDto>> GetCorrelationSummariesAsync(int page, int pageSize, CancellationToken cancellationToken)
        => await _context.GetCorrelationSummariesAsync(page, pageSize, cancellationToken);

    public async Task<List<MessageAuditLogDto>> GetEventsAsync(Guid correlationId, CancellationToken cancellationToken)
        => await _context.GetEventsByCorrelationIdAsync(correlationId, cancellationToken);
}
