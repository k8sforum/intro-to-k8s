using System.ComponentModel.DataAnnotations;

namespace mytravels.contract.Responses;

public class GetPointOfInterestResponse
{
    [Key]
    public int RowId { get; set; }
    public int PointOfInterestId { get; set; }
    public string Container { get; set; }
    public string OriginalFileName { get; set; }
    public string GeneratedBlobName { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime DateCreated { get; set; }
    public string FormattedAddress { get; set; }
    public bool ImageResized { get; set; }
    public int? TagId { get; set; }
    public string TagName { get; set; }
    public int PointOfInterestTypeId { get; set; }
    public string PointOfInterestType { get; set; }
    public string PointOfInterestKey { get; set; }
    public int PointOfInterestStatusId { get; set; }
    public string PointOfInterestStatus { get; set; }
    public string PrimaryColor { get; set; }
    public string SecondaryColor { get; set; }

}
