using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("MoyasarPaymentWebhooks")]
public class MoyasarPaymentWebhook
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string PaymentId { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = string.Empty;

    [Required]
    public int Amount { get; set; }

    [Required]
    [MaxLength(10)]
    public string Currency { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? PaymentMethod { get; set; }

    // Metadata fields
    public int? CustomerID { get; set; }

    [MaxLength(50)]
    public string? CustomerPhoneNumber { get; set; }

    public int? OfferID { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? PaymentValue { get; set; }

    // Original webhook payload
    [Required]
    public string WebhookPayload { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string EventType { get; set; } = string.Empty;

    public bool IsVerified { get; set; } = false;

    public bool IsProcessed { get; set; } = false;

    public DateTime? ProcessedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
