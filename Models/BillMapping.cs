using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("IntegrationVomIntegrationBillMappings")]
public class BillMapping
{
    [Key]
    public int Id { get; set; }

    public int BillId { get; set; }

    public int VomBillId { get; set; }

    public int LocationId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual Bill? Bill { get; set; }
}