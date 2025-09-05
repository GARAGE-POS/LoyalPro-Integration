using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("Items")]
public class Item
{
    [Key]
    public int ItemID { get; set; }
    
    public int RowID { get; set; } = 0;
    
    [Required]
    public int SubCatID { get; set; }
    
    public string? Name { get; set; }
    
    public string? NameOnReceipt { get; set; }
    
    public string? Description { get; set; }
    
    public string? ItemImage { get; set; }
    
    public string? Barcode { get; set; }
    
    public string? SKU { get; set; }
    
    public int? DisplayOrder { get; set; }
    
    public bool? SortByAlpha { get; set; }
    
    public double? Cost { get; set; }
    
    public double? Price { get; set; }
    
    public string? ItemType { get; set; }
    
    public int? UnitID { get; set; }
    
    public bool? FeaturedItem { get; set; } = false;
    
    public string? LastUpdatedBy { get; set; }
    
    public DateTime? LastUpdatedDate { get; set; }
    
    public int? StatusID { get; set; }
    
    public string? ItemTypeValue { get; set; }
    
    public bool? IsInventoryItem { get; set; }
    
    public bool? IsOpenItem { get; set; }
    
    public double? MinOpenPrice { get; set; }
    
    public int? OdooProductID { get; set; }
    
    // Navigation properties
    public virtual SubCategory? SubCategory { get; set; }
}