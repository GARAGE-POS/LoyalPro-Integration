using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("inv_BillDetail")]
public class BillDetail
{
    [Key]
    public int BillDetailID { get; set; }

    public int? BillID { get; set; }

    public int? ItemID { get; set; }

    public double? Cost { get; set; }

    public double? Price { get; set; }

    public int? Quantity { get; set; }

    public double? Total { get; set; }

    public int? StatusID { get; set; }

    [StringLength(500)]
    public string? Remarks { get; set; }

    public DateTime? LastUpdatedDate { get; set; }

    [StringLength(100)]
    public string? LastUpdatedBy { get; set; }

    public DateTime? CreatedOn { get; set; }

    [StringLength(100)]
    public string? CreatedBy { get; set; }

    // Navigation properties
    public virtual Bill? Bill { get; set; }
    public virtual Item? Item { get; set; }
}