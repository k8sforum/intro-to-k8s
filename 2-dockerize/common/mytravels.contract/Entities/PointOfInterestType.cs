using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mytravels.contract.Entities;

[Table("PointOfInterestTypes", Schema = "lookups")]
public class PointOfInterestType
{
    public int Id { get; set; }
    [StringLength(20)]
    public string Name { get; set; }
    [StringLength(30)]
    public string PrimaryColor { get; set; }
    [StringLength(30)]
    public string SecondaryColor { get; set; }
}
