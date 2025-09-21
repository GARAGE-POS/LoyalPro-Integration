using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("IntegrationVomIntegrationCategoryMappings")]
public class CategoryMapping
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int CategoryId { get; set; }

    [Required]
    public int VomCategoryId { get; set; }

    [Required]
    public int LocationId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Navigation property
    [ForeignKey("CategoryId")]
    public virtual Category? Category { get; set; }
}