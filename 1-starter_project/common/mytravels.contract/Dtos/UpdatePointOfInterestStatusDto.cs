using System.ComponentModel.DataAnnotations;

namespace mytravels.contract.Dtos;

public class UpdatePointOfInterestStatusDto
{
    [StringLength(40, ErrorMessage = "PointOfInterestKey cannot exceed 40 characters.")]
    [Required(ErrorMessage = "PointOfInterestKey is required.")]
    public string PointOfInterestKey { get; set; }

    [Required(ErrorMessage = "Status is required.")]
    public int StatusId { get; set; }
}
