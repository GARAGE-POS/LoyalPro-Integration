using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("Units", Schema = "dbo")]
public class Unit
{
    [Key]
    public int UnitID { get; set; }

    public int? RowID { get; set; }

    [Required]
    [StringLength(50)]
    [Column("Unit")]
    public string UnitName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }

    [StringLength(50)]
    public string? LastUpdatedBy { get; set; }

    public DateTime? LastUpdatedDate { get; set; }

    public int? StatusID { get; set; }

    public DateTime? CreatedOn { get; set; }

    [StringLength(100)]
    public string? CreatedBy { get; set; }

    // Computed property for symbol (using UnitName as fallback)
    [NotMapped]
    public string Symbol => UnitName;
}