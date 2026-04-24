using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace mytravels.contract.Entities;

[ExcludeFromCodeCoverage]
[Table("PointOfInterestTagAssociations")]
public class PointOfInterestTagAssociation
{
    public int Id { get; set; }
    public virtual int PointOfInterestId { get; set; }
    public virtual PointOfInterest PointOfInterest { get; set; }
    public virtual int TagId { get; set; }
    public virtual Tag Tag { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}