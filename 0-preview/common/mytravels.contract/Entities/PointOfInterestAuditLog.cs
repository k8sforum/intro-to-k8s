using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace mytravels.contract.Entities;

[ExcludeFromCodeCoverage]
[Table("PointOfInterestAuditLogs")]
public class PointOfInterestAuditLog
{
    public int Id { get; set; }
    [StringLength(100)]
    public string QueueName { get; set; }
    [StringLength(500)]
    public string Payload { get; set; }
    public bool Sucessful { get; set; } = false;
    [StringLength(500)]
    public string ErrorMessage { get; set; }
    public virtual int PointOfInterestId { get; set; }
    public virtual PointOfInterest PointOfInterest { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}