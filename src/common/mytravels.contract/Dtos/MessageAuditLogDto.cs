namespace mytravels.contract.Dtos;

public class MessageAuditLogDto
{
    public Guid CorrelationId { get; set; }
    public string ExchangeName { get; set; }
    public string EventType { get; set; }
    public int? PointOfInterestId { get; set; }
    public int RetryCount { get; set; }
    public string ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}
