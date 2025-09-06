using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("OrderCheckout")]
public class OrderCheckout
{
    [Key]
    public int OrderCheckOutID { get; set; }

    public int OrderID { get; set; }

    public string? SessionId { get; set; }

    public string? CheckoutDate { get; set; }

    public double? AmountTotal { get; set; }

    public int? OrderStatus { get; set; }

    public double? AmountPaid { get; set; }

    public double? AmountDiscount { get; set; }

    public double? Tax { get; set; }

    public double? GrandTotal { get; set; }

    public string? LastUpdateBy { get; set; }

    public DateTime? LastUpdateDT { get; set; }

    public string? Remarks { get; set; }

    public int LocationID { get; set; }

    public double? AmountComplementary { get; set; }

    public DateTime? CreatedOn { get; set; }

    public string? CreatedBy { get; set; }

    public int? WorkerID { get; set; }

    public int? AssistantID { get; set; }

    public int? PaymentMode { get; set; }

    public int? SubUserID { get; set; }

    public double? RefundedAmount { get; set; }

    public double? CashReturn { get; set; }

    public double? CardReturn { get; set; }

    public bool? IsPaid { get; set; }

    public bool? IsReady { get; set; }

    public int? Gratuity { get; set; }

    public double? ServiceCharges { get; set; }

    public double? DiscountPercent { get; set; }

    public double? TaxPercent { get; set; }

    public int? CreditCustomerID { get; set; }

    public double? PartialAmountReceived { get; set; }

    public bool? IsPartialPaid { get; set; }

    public string? DiscountCode { get; set; }

    public int? AppSourceID { get; set; }

    // Navigation properties
    public virtual Orders? Orders { get; set; }
    public virtual Location? Location { get; set; }
    public virtual ICollection<OrderCheckoutDetails> OrderCheckoutDetails { get; set; } = new List<OrderCheckoutDetails>();
}
