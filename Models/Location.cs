using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("Locations")]
public class Location
{
    [Key]
    public int LocationID { get; set; }
    
    public int RowID { get; set; } = 0;
    
    [Required]
    public string Name { get; set; } = string.Empty;
    
    public string? Descripiton { get; set; }
    
    public string? ArabicDescription { get; set; }
    
    public string? Address { get; set; }
    
    public string? ArabicAddress { get; set; }
    
    public string? ContactNo { get; set; }
    
    public string? Email { get; set; }
    
    public int? TimeZoneID { get; set; }
    
    public DateTime? DateFrom { get; set; }
    
    public DateTime? DateTo { get; set; }
    
    public string? CountryID { get; set; }
    
    public int? CityID { get; set; }
    
    public TimeSpan? Open_Time { get; set; }
    
    public TimeSpan? Close_Time { get; set; }
    
    [Required]
    public int UserID { get; set; }
    
    public int? LicenseID { get; set; }
    
    public bool? DeliveryServices { get; set; } = false;
    
    public double? DeliveryCharges { get; set; } = 0;
    
    public string? DeliveryTime { get; set; } = "0";
    
    public double? MinOrderAmount { get; set; } = 0;
    
    public string? Longitude { get; set; }
    
    public string? Latitude { get; set; }
    
    public string? LastUpdatedBy { get; set; }
    
    public DateTime? LastUpdatedDate { get; set; }
    
    public int? StatusID { get; set; }
    
    public int? CustomerStatusID { get; set; }
    
    [Required]
    public string CompanyCode { get; set; } = string.Empty;
    
    public int? LandmarkID { get; set; }
    
    public string? Gmaplink { get; set; }
    
    public string? ImageURL { get; set; }
    
    public bool? IsFeatured { get; set; }
    
    public string? ArabicName { get; set; }
    
    public string? Currency { get; set; }
    
    public string? VATNO { get; set; }
    
    public double? Tax { get; set; }
    
    public bool? AllowNegativeInventory { get; set; }
    
    public string? StreetName { get; set; }
    
    public string? PostalCode { get; set; }
    
    public string? BuildingNumber { get; set; }
    
    public string? District { get; set; }
    
    public DateTime? CreatedOn { get; set; }
    
    public string? CreatedBy { get; set; }
    
    // Navigation properties
    public virtual User? User { get; set; }
    public virtual ICollection<Category> Categories { get; set; } = new List<Category>();
}