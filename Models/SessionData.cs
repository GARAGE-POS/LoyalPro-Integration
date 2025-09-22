using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

public class SessionData
{
    public int LocationID { get; set; }
    public int UserID { get; set; }
    public string? Session { get; set; }
    public string? LocationName { get; set; }
    public string? CompanyTitle { get; set; }
    public string? CompanyAddress { get; set; }
    public string? CompanyPhones { get; set; }
    public string? CompanyEmail { get; set; }
    public string? Currency { get; set; }
    public string? CountryID { get; set; }
    public string? VATNo { get; set; }
    public string? Tax { get; set; }
}

public class SessionResponse
{
    public List<RoleData>? Roles { get; set; }
    public List<LocationData>? Locations { get; set; }
    public List<CarTypeData>? CarTypes { get; set; }
    public List<object>? Integrations { get; set; }
    public List<CreditCustomerData>? CreditCustomers { get; set; }
    public List<AppSourceData>? AppSources { get; set; }
    public object? ReceiptInfo { get; set; }
    public string? Tax { get; set; }
    public UserData? User { get; set; }
    public int Status { get; set; }
    public string? Description { get; set; }
    public List<string>? InspectionTypes { get; set; }
}

public class LocationData
{
    public int LocationID { get; set; }
    public string? Name { get; set; }
    public string? Descripiton { get; set; }
    public string? Address { get; set; }
    public string? ContactNo { get; set; }
    public string? Email { get; set; }
    public string? Currency { get; set; }
    public int TimeZoneID { get; set; }
    public string? CountryID { get; set; }
    public int UserID { get; set; }
    public int StatusID { get; set; }
}

public class UserData
{
    public int SubUserID { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? UserType { get; set; }
    public string? Designation { get; set; }
    public string? ImageURL { get; set; }
    public string? SellerName { get; set; }
    public string? Session { get; set; }
    public int LocationID { get; set; }
    public int SuperUserID { get; set; }
    public string? LocationName { get; set; }
    public string? VATNo { get; set; }
    public string? Tax { get; set; }
    public string? CompanyCode { get; set; }
    public List<LoginSessionData>? LoginSessions { get; set; }
}

public class RoleData
{
    public List<RoleData>? SubRoles { get; set; }
    public int GroupFormID { get; set; }
    public int? GroupID { get; set; }
    public int FormID { get; set; }
    public bool New { get; set; }
    public string? FormName { get; set; }
    public bool Edit { get; set; }
    public bool Remove { get; set; }
    public bool Access { get; set; }
    public int? StatusID { get; set; }
}

public class CarTypeData
{
    public int CarType { get; set; }
    public string? Name { get; set; }
    public string? Image { get; set; }
}

public class CreditCustomerData
{
    public int CreditCustomerID { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Description { get; set; }
    public string? LastVisit { get; set; }
    public string? CreatedDate { get; set; }
    public int UserID { get; set; }
    public string? VATNo { get; set; }
    public int? StatusID { get; set; }
}

public class AppSourceData
{
    public int SourceID { get; set; }
    public string? Name { get; set; }
    public string? ArabicName { get; set; }
}

public class LoginSessionData
{
    public List<DiscountData>? DiscountsList { get; set; }
    public int LocationID { get; set; }
    public string? Session { get; set; }
    public string? LocationName { get; set; }
    public string? CompanyTitle { get; set; }
    public string? CompanyAddress { get; set; }
    public string? CompanyPhones { get; set; }
    public string? CompanyFax { get; set; }
    public string? CompanyEmail { get; set; }
    public string? CompanyWebsite { get; set; }
    public string? Promotiontagline { get; set; }
    public string? Companytagline { get; set; }
    public string? CompanyLogoURL { get; set; }
    public string? Footer { get; set; }
    public string? FacebookLink { get; set; }
    public string? TwitterLink { get; set; }
    public string? InstagramLink { get; set; }
    public string? SnapchatLink { get; set; }
    public string? TikTokLink { get; set; }
    public string? Currency { get; set; }
    public string? CountryID { get; set; }
    public string? VATNo { get; set; }
    public string? Tax { get; set; }
    public string? QRTagline { get; set; }
    public string? QRLink { get; set; }
}

public class DiscountData
{
    public int DiscountID { get; set; }
    public string? Name { get; set; }
    public string? DiscountType { get; set; }
    public decimal Value { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public DateTime? FromTime { get; set; }
    public DateTime? ToTime { get; set; }
    public int LocationID { get; set; }
    public DateTime LastUpdatedDate { get; set; }
    public string? LastUpdatedBy { get; set; }
    public int StatusID { get; set; }
    public bool IsCouponCode { get; set; }
    public string? Code { get; set; }
    public int NoOfRedemption { get; set; }
}