using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("OrderDetail")]
public class OrderDetail
{
  [Key]
  [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
  public int OrderDetailID { get; set; }

  [Required]
  public int OrderID { get; set; }

  public int? ItemID { get; set; }

  public int? PackageID { get; set; }

  public double? Quantity { get; set; }

  public double? Price { get; set; }

  public double? Cost { get; set; }

  public int? StatusID { get; set; }

  public double? DiscountAmount { get; set; }

  public double? RefundQty { get; set; }

  public double? RefundAmount { get; set; }

  public string? OrderMode { get; set; }

  // Navigation properties
  [ForeignKey("OrderID")]
  public virtual Orders? Orders { get; set; }

  [ForeignKey("ItemID")]
  public virtual Item? Item { get; set; }

  [ForeignKey("PackageID")]
  public virtual Package? Package { get; set; }
}