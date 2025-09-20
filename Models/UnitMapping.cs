using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("IntegrationVomIntegrationVomUnitMappings")]
public class UnitMapping
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UnitId { get; set; }

    [Required]
    public int VomUnitId { get; set; }

    [Required]
    public int LocationId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Navigation property
    [ForeignKey("UnitId")]
    public virtual Unit? Unit { get; set; }
}