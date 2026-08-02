using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace mytravels.contract.Entities;

[ExcludeFromCodeCoverage]
[Table("Tags")]
public class Tag
{
    public int Id { get; set; }
    [StringLength(30)]
    //[Index(IsUnique = true)]
    public string Name { get; set; }
    public DateTime DateCreated { get; set; } = DateTime.UtcNow;
}