using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("SessionInfo")]
public class SessionInfo
{
    [Key]
    public int RowId { get; set; }

    [Required]
    public string SessionId { get; set; } = string.Empty;

    [Required]
    public int SubUserId { get; set; }

    [Required]
    public int StatusID { get; set; }

    [Required]
    public int UTC { get; set; }

    public DateTime? StartDT { get; set; }

    public DateTime? EndDT { get; set; }

    public TimeSpan? OpenTime { get; set; }

    public TimeSpan? CloseTime { get; set; }

    public DateTime? LogoutDT { get; set; }

    public DateTime? LoginDT { get; set; }

    public DateTime? CreatedOn { get; set; }

    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    [Required]
    public int LocationID { get; set; }

    [MaxLength(50)]
    public string? Currency { get; set; }

    // Navigation properties
    [ForeignKey("LocationID")]
    public Location? Location { get; set; }

    [ForeignKey("SubUserId")]
    public SubUser? SubUser { get; set; }
}