using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("Users")]
public class User
{
    [Key]
    public int UserID { get; set; }
    
    public int RowID { get; set; } = 0;
    
    public int? PackageInfoID { get; set; }
    
    public string? UserName { get; set; }
    
    public string? FirstName { get; set; }
    
    public string? LastName { get; set; }
    
    public string? ImagePath { get; set; }
    
    public string? Password { get; set; }
    
    public string? Company { get; set; }
    
    public string? BusinessType { get; set; }
    
    public string? Email { get; set; }
    
    public string? ContactNo { get; set; }
    
    public string? Address { get; set; }
    
    public int? CityID { get; set; }
    
    public string? CountryID { get; set; }
    
    public string? Website { get; set; }
    
    public bool? Subscribe { get; set; }
    
    public int? RoleID { get; set; }
    
    public int? TimeZoneID { get; set; }
    
    public string? LastUpdatedBy { get; set; }
    
    public DateTime? LastUpdatedDate { get; set; }
    
    public int? StatusID { get; set; }
    
    public string? CompanyCode { get; set; }
    
    public DateTime? CreatedDate { get; set; }
    
    public string? States { get; set; }
    
    public string? Zipcode { get; set; }
    
    public string? VATNO { get; set; }
    
    public double? Tax { get; set; }
    
    public bool? IsSMSCheckoutAddOn { get; set; } = false;
    
    public bool? AllowNegativeInventory { get; set; }
    
    public bool? IsOdoo { get; set; }
    
    public bool? IsAccountingAddons { get; set; }
    
    public bool? IsGarageGo { get; set; }
    
    public bool? IsCashier { get; set; }
    
    public string? BrandThumbnailImage { get; set; }
    
    public int? LoginSessionTime { get; set; } = 0;
    
    public bool? IsYakeen { get; set; }
    
    public bool? IsMojaz { get; set; }
    
    public bool? IsDefaultCar { get; set; }
    
    public string? PaymentLink { get; set; }
    
    public string? SMSProvider { get; set; }
    
    public bool? IsCheckLitreMandatory { get; set; }
    
    // Navigation properties
    public virtual ICollection<Location> Locations { get; set; } = new List<Location>();
}