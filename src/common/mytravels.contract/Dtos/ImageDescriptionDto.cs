namespace mytravels.contract.Dtos;

public class ImageDescriptionDto
{
    public string Description { get; set; }
    public List<string> Tags { get; set; } = new();
}
