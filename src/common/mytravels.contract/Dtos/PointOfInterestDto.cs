using System.ComponentModel.DataAnnotations;

namespace mytravels.contract.Dtos;

public class PointOfInterestDto
{
    public int Id { get; set; }
    public string PointOfInterestKey { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    public DateTime? DateTaken { get; set; }
    [StringLength(300)]
    public string FormattedAddress { get; set; }
    public List<TagDto> Tags { get; set; } = new();
}
