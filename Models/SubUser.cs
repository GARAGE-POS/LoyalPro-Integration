using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("SubUsers")]
public class SubUser
{
    [Key]
    public int SubUserID { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? UserType { get; set; }

    // public int SuperUserID { get; set; }

    public int StatusID { get; set; }

    // Navigation property
    // [ForeignKey("SuperUserID")]
    // public User? SuperUser { get; set; }
}