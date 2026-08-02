using System.ComponentModel.DataAnnotations;

namespace mytravels.contract.Dtos;

public class SaveCoordinatesDto
{
    [Range(-90, 90, ErrorMessage = "Latitude must be between -90 and 90.")]
    [Required(ErrorMessage = "Latitude is required.")]
    public double Latitude { get; set; }

    [Range(-180, 180, ErrorMessage = "Longitude must be between -180 and 180.")]
    [Required(ErrorMessage = "Longitude is required.")]
    public double Longitude { get; set; }

    [Range(1, 3, ErrorMessage = "Point of interest type id must be between 1 and 3.")]
    [Required(ErrorMessage = "Point of interest type id is required.")]
    public int PointOfInterestTypeId { get; set; }

    public string FormattedAddress { get; set; }
}
