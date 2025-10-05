using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("IntegrationBoukakCustomerMappings")]
public class BoukakCustomerMapping
{
    [Key]
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public string BoukakCustomerId { get; set; } = string.Empty;

    public string BoukakCardId { get; set; } = string.Empty;

    public int LocationId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    [ForeignKey("CustomerId")]
    public virtual Customer? Customer { get; set; }

    [ForeignKey("LocationId")]
    public virtual Location? Location { get; set; }
}
