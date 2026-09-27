using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace mytravels.contract.Entities;

[ExcludeFromCodeCoverage]
[Table("FailedMessages")]
public class FailedMessage
{
    public int Id { get; set; }
    public Guid CorrelationId { get; set; }
    [StringLength(100)]
    public string OriginalExchange { get; set; }
    public string Payload { get; set; }
    public int? PointOfInterestId { get; set; }
    [StringLength(500)]
    public string ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime FailedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}
