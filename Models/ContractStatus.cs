using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("IntegrationSadeqContracts")]
public class ContractStatus
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string CompanyCode { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string CompanyName { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Terminals { get; set; }

    [Required]
    [StringLength(20)]
    public string NationalId { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the Sadeq request was sent successfully
    /// </summary>
    public bool SadqSent { get; set; } = false;

    /// <summary>
    /// Indicates whether the contract has been signed
    /// </summary>
    public bool Signed { get; set; } = false;

    /// <summary>
    /// Sadeq Envelope ID returned from API
    /// </summary>
    [StringLength(255)]
    public string? EnvelopId { get; set; }

    /// <summary>
    /// Sadeq Document ID returned from API
    /// </summary>
    [StringLength(255)]
    public string? DocumentId { get; set; }

    /// <summary>
    /// Template ID used for template-based contracts (optional)
    /// </summary>
    [StringLength(255)]
    public string? TemplateId { get; set; }

    /// <summary>
    /// Original PDF filename for PDF-based contracts (optional)
    /// </summary>
    [StringLength(500)]
    public string? PdfFileName { get; set; }

    /// <summary>
    /// When the contract was created/sent
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the contract status was last updated
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// When the contract was signed (if signed)
    /// </summary>
    public DateTime? SignedAt { get; set; }

    /// <summary>
    /// Additional notes or comments
    /// </summary>
    [StringLength(1000)]
    public string? Notes { get; set; }

    /// <summary>
    /// Error message if sending failed
    /// </summary>
    [StringLength(1000)]
    public string? ErrorMessage { get; set; }
}
