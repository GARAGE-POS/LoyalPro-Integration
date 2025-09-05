using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("Customers")]
public class Customer
{
    [Key]
    public int CustomerID { get; set; }
    
    public int RowID { get; set; } = 0;
    
    public string? UserName { get; set; }
    
    public string? FullName { get; set; }
    
    public string? Password { get; set; }
    
    public string? Barcode { get; set; }
    
    public string? Email { get; set; }
    
    public string? DOB { get; set; }
    
    public string? Sex { get; set; }
    
    [Required]
    public string Mobile { get; set; } = string.Empty;
    
    public string? CardNo { get; set; }
    
    public string? LastUpdatedBy { get; set; }
    
    public DateTime? LastUpdatedDate { get; set; }
    
    public double? Points { get; set; }
    
    public string? ImagePath { get; set; }
    
    public int? StatusID { get; set; }
    
    public double? Redem { get; set; }
    
    public DateTime? LastVisit { get; set; }
    
    public int? UserID { get; set; }
    
    public int? LocationID { get; set; }
    
    public bool? IsEmail { get; set; }
    
    public bool? IsSMS { get; set; }
    
    public DateTime? CreatedOn { get; set; }
    
    public string? CreatedBy { get; set; }
    
    public bool? IsSignUp { get; set; } = false;
    
    public string? City { get; set; }
    
    public string? Country { get; set; }
}