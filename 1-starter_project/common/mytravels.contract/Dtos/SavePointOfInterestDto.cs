namespace mytravels.contract.Dtos;

public class SavePointOfInterestDto
{
    public int PointOfInterestId { get; set; }
    public List<string> Tags { get; set; } = new List<string>();
}
