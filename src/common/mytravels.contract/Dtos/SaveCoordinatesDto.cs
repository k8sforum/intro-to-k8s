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

    public string FormattedAddress { get; set; }
}
