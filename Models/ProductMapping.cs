using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("IntegrationVomIntegrationProductMappings")]
public class ProductMapping
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int ItemId { get; set; }

    [Required]
    public int VomProductId { get; set; }

    [Required]
    public int LocationId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Navigation property
    [ForeignKey("ItemId")]
    public virtual Item? Item { get; set; }
}