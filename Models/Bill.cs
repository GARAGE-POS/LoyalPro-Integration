using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("inv_Bill")]
public class Bill
{
    [Key]
    public int BillID { get; set; }

    public int? PurchaseOrderID { get; set; }

    [StringLength(50)]
    public string? BillNo { get; set; }

    public DateTime? Date { get; set; }

    public DateTime? DueDate { get; set; }

    [StringLength(250)]
    public string? Remarks { get; set; }

    public double? SubTotal { get; set; }

    public double? Discount { get; set; }

    public double? Tax { get; set; }

    public double? Total { get; set; }

    public string? ImagePath { get; set; }

    public int? PaymentStatus { get; set; }

    public DateTime? LastUpdatedDate { get; set; }

    [StringLength(100)]
    public string? LastUpdatedBy { get; set; }

    public DateTime? CreateOn { get; set; }

    [StringLength(100)]
    public string? CreatedBy { get; set; }

    public int? StatusID { get; set; }

    public int LocationID { get; set; }

    public int? StoreID { get; set; }

    public int? SupplierID { get; set; }

    // Navigation properties
    public virtual Location? Location { get; set; }
    public virtual Supplier? Supplier { get; set; }
    public virtual ICollection<BillDetail> BillDetails { get; set; } = new List<BillDetail>();
}