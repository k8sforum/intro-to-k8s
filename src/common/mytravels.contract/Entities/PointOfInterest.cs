using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace mytravels.contract.Entities;

[ExcludeFromCodeCoverage]
[Table("PointOfInterests")]
public class PointOfInterest
{
    public int Id { get; set; }
    [StringLength(40)]
    public string PointOfInterestKey { get; set; }
    [StringLength(250)]
    public string Container { get; set; }
    [StringLength(250)]
    public string OriginalFileName { get; set; }
    [StringLength(250)]
    public string GeneratedBlobName { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    [StringLength(300)]
    public string FormattedAddress { get; set; }
    public bool ImageResized { get; set; }
    public virtual int PointOfInterestTypeId { get; set; }
    public virtual PointOfInterestType PointOfInterestType { get; set; }
    public virtual int PointOfInterestStatusId { get; set; }
    public virtual PointOfInterestStatus PointOfInterestStatus { get; set; }
    public virtual HashSet<PointOfInterestTagAssociation> PointOfInterestTagAssociations { get; set; }
    public virtual HashSet<PointOfInterestAuditLog> PointOfInterestAuditLogs { get; set; }
    public DateTime? DateUpdated { get; set; }
    public Guid? UpdatedBy { get; set; }
    [StringLength(500)]
    public string Reason { get; set; }
}