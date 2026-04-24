using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace mytravels.contract.Entities;

[Table("PointOfInterestStatuses", Schema = "lookups")]
public class PointOfInterestStatus
{
    public int Id { get; set; }
    [StringLength(20)]
    public string Name { get; set; }
    [StringLength(30)]
    public string PrimaryColor { get; set; }
    [StringLength(30)]
    public string SecondaryColor { get; set; }
}
