using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Karage.Functions.Models;

namespace Functions.Models;

[Table("inv_Reconciliation")]
public class Reconciliation
{
    [Key]
    public int ReconciliationID { get; set; }

    public int? PurchaseOrderID { get; set; }

    [MaxLength(50)]
    public string? Code { get; set; }

    public DateTime? Date { get; set; }

    [MaxLength(250)]
    public string? Reason { get; set; }

    public DateTime? LastUpdatedDate { get; set; }

    [MaxLength(100)]
    public string? LastUpdatedBy { get; set; }

    public int? StatusID { get; set; }

    public int LocationID { get; set; }

    public int? StoreID { get; set; }

    public int? UserID { get; set; }

    // Navigation properties
    public Karage.Functions.Models.Location Location { get; set; } = null!;
    public ICollection<ReconciliationDetail> ReconciliationDetails { get; set; } = new List<ReconciliationDetail>();
}