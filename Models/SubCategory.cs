using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("SubCategory")]
public class SubCategory
{
    [Key]
    public int SubCategoryID { get; set; }
    
    public int RowID { get; set; } = 0;
    
    [Required]
    public int CategoryID { get; set; }
    
    public string? Name { get; set; }
    
    public string? AlternateName { get; set; }
    
    public string? Description { get; set; }
    
    public string? SubImage { get; set; }
    
    public int? DisplayOrder { get; set; }
    
    public int? PrinterID { get; set; }
    
    public bool? SortByAlpha { get; set; }
    
    public string? LastUpdatedBy { get; set; }
    
    public DateTime? LastUpdatedDate { get; set; }
    
    public int? StatusID { get; set; }
    
    // Navigation properties
    public virtual Category? Category { get; set; }
    public virtual ICollection<Item> Items { get; set; } = new List<Item>();
}