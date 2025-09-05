using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("OrderCheckout")]
public class OrderCheckout
{
    [Key]
    public int OrderCheckOutID { get; set; }

    public int OrderID { get; set; }

    public string? CheckoutDate { get; set; }

    public float? AmountPaid { get; set; }

    public float? AmountDiscount { get; set; }

    // Navigation properties
    public virtual Orders? Orders { get; set; }
    public virtual ICollection<OrderCheckoutDetails> OrderCheckoutDetails { get; set; } = new List<OrderCheckoutDetails>();
}
