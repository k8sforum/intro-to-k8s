namespace mytravels.contract.Dtos;

public class CorrelationSummaryDto
{
    public Guid CorrelationId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime LastEventAt { get; set; }
    public int EventCount { get; set; }
    public bool HasFailure { get; set; }
}
