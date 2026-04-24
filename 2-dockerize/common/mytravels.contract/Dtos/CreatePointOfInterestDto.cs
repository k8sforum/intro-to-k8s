namespace mytravels.contract.Dtos;

public class CreatePointOfInterestDto
{
    public string OriginalFileName { get; set; }
    public string BlobName { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int PointOfInterestTypeId { get; set; }
    public string FormattedAddress { get; set; }
}
