using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("Category", Schema = "dbo")]
public class Category
{
    [Key]
    public int CategoryID { get; set; }

    public int RowID { get; set; } = 0;

    [StringLength(80)]
    public string? Name { get; set; }

    [StringLength(80)]
    public string? AlternateName { get; set; }

    [StringLength(100)]
    public string? Description { get; set; }

    [Column("Image", TypeName = "nvarchar(MAX)")]
    public string? Image { get; set; }

    public int? DisplayOrder { get; set; }

    public bool? SortByAlpha { get; set; }

    [StringLength(50)]
    public string? LastUpdatedBy { get; set; }

    public DateTime? LastUpdatedDate { get; set; }

    public int? StatusID { get; set; }

    public int? LocationID { get; set; }
    
    // Navigation properties
    public virtual Location? Location { get; set; }
    public virtual ICollection<SubCategory> SubCategories { get; set; } = new List<SubCategory>();
}