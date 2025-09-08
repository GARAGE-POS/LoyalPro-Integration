using System.ComponentModel.DataAnnotations.Schema;

namespace functions.Models
{
    [Table("MapUniqueItemID")]
    public class MapUniqueItemID
    {
        public required string ProductName { get; set; }
        public int ItemID { get; set; }
        public int LocationID { get; set; }
        public required string LocationName { get; set; }
        public int UniqueItemID { get; set; }
    }
}
