using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("OrderCheckoutDetails")]
public class OrderCheckoutDetails
{
    [Key]
    public int OrderCheckOutDetailID { get; set; }

    public int OrderCheckoutID { get; set; }

    public double? AmountPaid { get; set; }

    public double? AmountDiscount { get; set; }

    public DateTime? CheckoutDate { get; set; }

    // Navigation properties
    public virtual OrderCheckout? OrderCheckout { get; set; }
}