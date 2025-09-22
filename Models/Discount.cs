using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("Discount", Schema = "dbo")]
public class Discount
{
    [Key]
    public int DiscountID { get; set; }

    [StringLength(300)]
    public string? Name { get; set; }

    [StringLength(50)]
    public string? DiscountType { get; set; }

    public double? Value { get; set; }

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public TimeSpan? FromTime { get; set; }

    public TimeSpan? ToTime { get; set; }

    [Required]
    public int LocationID { get; set; }

    public DateTime? LastUpdatedDate { get; set; }

    [StringLength(100)]
    public string? LastUpdatedBy { get; set; }

    public int? StatusID { get; set; }

    [StringLength(50)]
    public string? DiscountBy { get; set; }

    public bool? IsCouponCode { get; set; }

    [StringLength(50)]
    public string? Code { get; set; }

    public int? NoOfRedemption { get; set; }

    // Navigation property for the foreign key relationship
    [ForeignKey("LocationID")]
    public virtual Location? Location { get; set; }
}