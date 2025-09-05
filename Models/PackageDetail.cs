using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("PackageDetails")]
public class PackageDetail
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int PackageDetailID { get; set; }

    public int? PackageID { get; set; }

    public int? ItemID { get; set; }

    public float? Quantity { get; set; }

    public float? Discount { get; set; }

    public float? Cost { get; set; }

    public string? DiscountType { get; set; }  // Corrected typo from SQL definition

    public float? Price { get; set; }

    public int? StatusID { get; set; }

    // Navigation properties
    [ForeignKey("PackageID")]
    public virtual Package? Package { get; set; }

    [ForeignKey("ItemID")]
    public virtual Item? Item { get; set; }
}