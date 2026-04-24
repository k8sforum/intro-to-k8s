using System.ComponentModel.DataAnnotations;

namespace mytravels.contract.Dtos;

public class PointOfInterestDto
{
    public int Id { get; set; }
    public string PointOfInterestKey { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int PointOfInterestTypeId { get; set; }
    public string PointOfInterestType { get; set; }
    public int PointOfInterestStatusId { get; set; }
    public string PointOfInterestStatus { get; set; }
    public string PrimaryColor { get; set; }
    public string SecondaryColor { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
    [StringLength(300)]
    public string FormattedAddress { get; set; }
    public List<TagDto> Tags { get; set; } = new();
}
