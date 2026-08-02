using System.ComponentModel.DataAnnotations;

namespace mytravels.contract.Dtos;

public class TagDto
{
    public int Id { get; set; }
    [StringLength(30)]
    public string Name { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}
