using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("Category")]
public class Category
{
    [Key]
    public int CategoryID { get; set; }
    
    public int RowID { get; set; } = 0;
    
    public string? Name { get; set; }
    
    public string? AlternateName { get; set; }
    
    public string? Description { get; set; }
    
    [Column("Image")]
    public string? Image { get; set; }
    
    public int? DisplayOrder { get; set; }
    
    public bool? SortByAlpha { get; set; }
    
    public string? LastUpdatedBy { get; set; }
    
    public DateTime? LastUpdatedDate { get; set; }
    
    public int? StatusID { get; set; }
    
    public int? LocationID { get; set; }
    
    // Navigation properties
    public virtual Location? Location { get; set; }
    public virtual ICollection<SubCategory> SubCategories { get; set; } = new List<SubCategory>();
}