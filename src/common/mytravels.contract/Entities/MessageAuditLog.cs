using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace mytravels.contract.Entities;

[ExcludeFromCodeCoverage]
[Table("MessageAuditLogs")]
public class MessageAuditLog
{
    public int Id { get; set; }
    public Guid CorrelationId { get; set; }
    [StringLength(100)]
    public string ExchangeName { get; set; }
    [StringLength(30)]
    public string EventType { get; set; }
    public int? PointOfInterestId { get; set; }
    public int RetryCount { get; set; }
    [StringLength(500)]
    public string ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
