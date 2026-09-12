using mytravels.contract.Dtos;

namespace mytravels.contract.Interfaces;

public interface ITraceabilityService
{
    Task<List<CorrelationSummaryDto>> GetCorrelationSummariesAsync(int page, int pageSize, CancellationToken cancellationToken);
    Task<List<MessageAuditLogDto>> GetEventsAsync(Guid correlationId, CancellationToken cancellationToken);
}
