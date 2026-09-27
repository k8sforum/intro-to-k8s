namespace mytravels.contract.Dtos;

public class FailedMessageDto
{
    public int Id { get; set; }
    public Guid CorrelationId { get; set; }
    public string OriginalExchange { get; set; }
    public int? PointOfInterestId { get; set; }
    public string ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime FailedAt { get; set; }
}
