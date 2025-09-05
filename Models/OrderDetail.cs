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

  public float? Quantity { get; set; }

  public float? Price { get; set; }

  public float? Cost { get; set; }

  public int? StatusID { get; set; }

  public float? DiscountAmount { get; set; }

  // Navigation properties
  [ForeignKey("OrderID")]
  public virtual Orders? Orders { get; set; }

  [ForeignKey("ItemID")]
  public virtual Item? Item { get; set; }

  [ForeignKey("PackageID")]
  public virtual Package? Package { get; set; }
}