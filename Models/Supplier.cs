using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("Supplier", Schema = "dbo")]
public class Supplier
{
    [Key]
    public int SupplierID { get; set; }

    [StringLength(200)]
    public string? Name { get; set; }

    [StringLength(150)]
    public string? Email { get; set; }

    [StringLength(50)]
    public string? Phone { get; set; }

    [StringLength(150)]
    public string? Website { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(500)]
    public string? Remarks { get; set; }

    [StringLength(200)]
    public string? CompanyName { get; set; }

    [StringLength(150)]
    public string? ContactPerson { get; set; }

    public int? StatusID { get; set; }

    public DateTime? LastUpdatedDate { get; set; }

    [StringLength(100)]
    public string? LastUpdatedBy { get; set; }

    public DateTime? CreatedOn { get; set; }

    [StringLength(100)]
    public string? CreatedBy { get; set; }

    public int? UserID { get; set; }

    [StringLength(50)]
    [Column("Type")]
    public string? Type { get; set; }

    // Navigation property
    [ForeignKey("UserID")]
    public virtual User? User { get; set; }
}