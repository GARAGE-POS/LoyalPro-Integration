using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Karage.Functions.Models;

namespace Functions.Models;

[Table("inv_ReconciliationDetail")]
public class ReconciliationDetail
{
    [Key]
    public int ReconciliationDetailID { get; set; }

    public int? ReconciliationID { get; set; }

    public int ItemID { get; set; }

    public double? Cost { get; set; }

    public double? Price { get; set; }

    public int? Quantity { get; set; }

    public double? Total { get; set; }

    public int? StatusID { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    // Navigation properties
    public Reconciliation? Reconciliation { get; set; }
    public Karage.Functions.Models.Item Item { get; set; } = null!;
}