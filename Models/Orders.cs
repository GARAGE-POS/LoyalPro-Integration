using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models;

[Table("Orders")]
public class Orders
{
  [Key]
  public int OrderID { get; set; }

  public int? StatusID { get; set; }

  public DateTime? OrderCreatedDT { get; set; }

  public int LocationID { get; set; }

  public int? CustomerID { get; set; }

  // Navigation properties
  public virtual Customer? Customer { get; set; }
  public virtual Location? Location { get; set; }
  public virtual ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
  public virtual OrderCheckout? OrderCheckout { get; set; }
}