using System.ComponentModel.DataAnnotations;

namespace mytravels.contract.Dtos;

public class LookupTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    [StringLength(30)]
    public string PrimaryColor { get; set; }
    [StringLength(30)]
    public string SecondaryColor { get; set; }
}
