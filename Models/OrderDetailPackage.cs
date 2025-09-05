using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karage.Functions.Models
{
    [Table("OrderDetailPackage", Schema = "dbo")]
    public class OrderDetailPackage
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int OrderPkgDetailID { get; set; }

        [Required]
        public int OrderDetailID { get; set; }

        public int? ItemID { get; set; }

        [StringLength(300)]
        public string? Name { get; set; }

        public float? Quantity { get; set; }

        public float? Cost { get; set; }

        public float? Price { get; set; }

        public int? StatusID { get; set; }

        // Navigation properties (optional, for EF relationships)
        [ForeignKey("OrderDetailID")]
        public virtual OrderDetail? OrderDetail { get; set; }

        [ForeignKey("ItemID")]
        public virtual Item? Item { get; set; }
    }
}