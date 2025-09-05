using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("Packages")]
public class Package
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int PackageID { get; set; }

    public int? SubCategoryID { get; set; }

    public string? Name { get; set; }

    public string? ArabicName { get; set; }

    public string? Description { get; set; }

    public float? Price { get; set; }

    public float? Cost { get; set; }

    public string? SKU { get; set; }

    public string? Barcode { get; set; }

    public string? Image { get; set; }

    public int? UserID { get; set; }

    public int? StatusID { get; set; }

    public string? LastUpdatedBy { get; set; }

    public DateTime? LastUpdatedDate { get; set; }

    public int? LocationID { get; set; }

    // Navigation properties
    [ForeignKey("LocationID")]
    public virtual Location? Location { get; set; }

    [ForeignKey("SubCategoryID")]
    public virtual SubCategory? SubCategory { get; set; }

    [ForeignKey("UserID")]
    public virtual User? User { get; set; }

    // Relation to PackageDetail (inverse navigation)
    public virtual ICollection<PackageDetail>? PackageDetails { get; set; }
}