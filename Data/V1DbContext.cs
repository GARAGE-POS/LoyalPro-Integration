using Microsoft.EntityFrameworkCore;
using Karage.Functions.Models;
using Functions.Models;

namespace Karage.Functions.Data;

public class V1DbContext : DbContext
{
    public V1DbContext(DbContextOptions<V1DbContext> options) : base(options)
    {
    }

    public DbSet<Customer> Customers { get; set; }
    public DbSet<Item> Items { get; set; }
    public DbSet<SubCategory> SubCategories { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Location> Locations { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Orders> Orders { get; set; }
    public DbSet<OrderDetail> OrderDetails { get; set; }
    public DbSet<OrderCheckout> OrderCheckouts { get; set; }
    public DbSet<OrderCheckoutDetails> OrderCheckoutDetails { get; set; }
    public DbSet<OrderDetailPackage> OrderDetailPackages { get; set; }
    public DbSet<Package> Packages { get; set; }
    public DbSet<PackageDetail> PackageDetails { get; set; }
    public DbSet<functions.Models.MapUniqueItemID> MapUniqueItemIDs { get; set; }
    public DbSet<Unit> Units { get; set; }
    public DbSet<UnitMapping> UnitMappings { get; set; }
    public DbSet<Supplier> Suppliers { get; set; }
    public DbSet<SupplierMapping> SupplierMappings { get; set; }
    public DbSet<CategoryMapping> CategoryMappings { get; set; }
    public DbSet<ProductMapping> ProductMappings { get; set; }
    public DbSet<Discount> Discounts { get; set; }
    public DbSet<SessionInfo> SessionInfos { get; set; }
    public DbSet<SubUser> SubUsers { get; set; }
    public DbSet<Bill> Bills { get; set; }
    public DbSet<BillDetail> BillDetails { get; set; }
    public DbSet<BillMapping> BillMappings { get; set; }
    public DbSet<Reconciliation> Reconciliations { get; set; }
    public DbSet<ReconciliationDetail> ReconciliationDetails { get; set; }
    public DbSet<MoyasarPaymentWebhook> MoyasarPaymentWebhooks { get; set; }
    public DbSet<BoukakCustomerMapping> BoukakCustomerMappings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

    // Configure relationships
        // Configure one-to-one relationship between OrderCheckout and Orders
        modelBuilder.Entity<OrderCheckout>()
            .HasOne(oc => oc.Orders)
            .WithOne(o => o.OrderCheckout)
            .HasForeignKey<OrderCheckout>(oc => oc.OrderID)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Item>()
            .HasOne(i => i.SubCategory)
            .WithMany(sc => sc.Items)
            .HasForeignKey(i => i.SubCatID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SubCategory>()
            .HasOne(sc => sc.Category)
            .WithMany(c => c.SubCategories)
            .HasForeignKey(sc => sc.CategoryID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Category>()
            .HasOne(c => c.Location)
            .WithMany(l => l.Categories)
            .HasForeignKey(c => c.LocationID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Location>()
            .HasOne(l => l.User)
            .WithMany(u => u.Locations)
            .HasForeignKey(l => l.UserID)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure OrderDetail relationships
        modelBuilder.Entity<OrderDetail>()
            .HasOne(od => od.Orders)
            .WithMany()
            .HasForeignKey(od => od.OrderID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<OrderDetail>()
            .HasOne(od => od.Item)
            .WithMany()
            .HasForeignKey(od => od.ItemID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<OrderDetail>()
            .HasOne(od => od.Package)
            .WithMany()
            .HasForeignKey(od => od.PackageID)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure OrderDetailPackage relationships
        modelBuilder.Entity<OrderDetailPackage>()
            .HasOne(odp => odp.OrderDetail)
            .WithMany()
            .HasForeignKey(odp => odp.OrderDetailID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<OrderDetailPackage>()
            .HasOne(odp => odp.Item)
            .WithMany()
            .HasForeignKey(odp => odp.ItemID)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure Package relationships
        modelBuilder.Entity<Package>()
            .HasOne(p => p.Location)
            .WithMany()
            .HasForeignKey(p => p.LocationID)
            .OnDelete(DeleteBehavior.Restrict);

    // Configure MapUniqueItemID as a keyless entity (view/table without PK)
    modelBuilder.Entity<functions.Models.MapUniqueItemID>().HasNoKey();

        modelBuilder.Entity<Package>()
            .HasOne(p => p.SubCategory)
            .WithMany()
            .HasForeignKey(p => p.SubCategoryID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Package>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserID)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure PackageDetail relationships
        modelBuilder.Entity<PackageDetail>()
            .HasOne(pd => pd.Package)
            .WithMany(p => p.PackageDetails)
            .HasForeignKey(pd => pd.PackageID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PackageDetail>()
            .HasOne(pd => pd.Item)
            .WithMany()
            .HasForeignKey(pd => pd.ItemID)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure precision for double/float properties
        modelBuilder.Entity<Customer>()
            .Property(c => c.Points)
            .HasColumnType("float");

        modelBuilder.Entity<Customer>()
            .Property(c => c.Redem)
            .HasColumnType("float");

        modelBuilder.Entity<Item>()
            .Property(i => i.Cost)
            .HasColumnType("float");

        modelBuilder.Entity<Item>()
            .Property(i => i.Price)
            .HasColumnType("float");

        modelBuilder.Entity<Item>()
            .Property(i => i.MinOpenPrice)
            .HasColumnType("float");

        modelBuilder.Entity<Location>()
            .Property(l => l.DeliveryCharges)
            .HasColumnType("float");

        modelBuilder.Entity<Location>()
            .Property(l => l.MinOrderAmount)
            .HasColumnType("float");

        modelBuilder.Entity<Location>()
            .Property(l => l.Tax)
            .HasColumnType("float");

        modelBuilder.Entity<User>()
            .Property(u => u.Tax)
            .HasColumnType("float");

        // Configure float properties for new entities
        modelBuilder.Entity<OrderDetail>()
            .Property(od => od.Quantity)
            .HasColumnType("float");

        modelBuilder.Entity<OrderDetail>()
            .Property(od => od.Price)
            .HasColumnType("float");

        modelBuilder.Entity<OrderDetail>()
            .Property(od => od.Cost)
            .HasColumnType("float");

        modelBuilder.Entity<OrderDetail>()
            .Property(od => od.DiscountAmount)
            .HasColumnType("float");

        modelBuilder.Entity<OrderDetailPackage>()
            .Property(odp => odp.Quantity)
            .HasColumnType("float");

        modelBuilder.Entity<OrderDetailPackage>()
            .Property(odp => odp.Cost)
            .HasColumnType("float");

        modelBuilder.Entity<OrderDetailPackage>()
            .Property(odp => odp.Price)
            .HasColumnType("float");

        modelBuilder.Entity<Package>()
            .Property(p => p.Price)
            .HasColumnType("float");

        modelBuilder.Entity<Package>()
            .Property(p => p.Cost)
            .HasColumnType("float");

        modelBuilder.Entity<PackageDetail>()
            .Property(pd => pd.Quantity)
            .HasColumnType("float");

        modelBuilder.Entity<PackageDetail>()
            .Property(pd => pd.Discount)
            .HasColumnType("float");

        modelBuilder.Entity<PackageDetail>()
            .Property(pd => pd.Cost)
            .HasColumnType("float");

        modelBuilder.Entity<PackageDetail>()
            .Property(pd => pd.Price)
            .HasColumnType("float");

        modelBuilder.Entity<Discount>()
            .Property(d => d.Value)
            .HasColumnType("float");

        // Configure Bill relationships
        modelBuilder.Entity<Bill>()
            .HasOne(b => b.Location)
            .WithMany()
            .HasForeignKey(b => b.LocationID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Bill>()
            .HasOne(b => b.Supplier)
            .WithMany()
            .HasForeignKey(b => b.SupplierID)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure BillDetail relationships
        modelBuilder.Entity<BillDetail>()
            .HasOne(bd => bd.Bill)
            .WithMany(b => b.BillDetails)
            .HasForeignKey(bd => bd.BillID)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BillDetail>()
            .HasOne(bd => bd.Item)
            .WithMany()
            .HasForeignKey(bd => bd.ItemID)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure BillMapping relationships
        modelBuilder.Entity<BillMapping>()
            .HasOne(bm => bm.Bill)
            .WithMany()
            .HasForeignKey(bm => bm.BillId)
            .OnDelete(DeleteBehavior.Restrict);

        // Configure float properties for Bill entities
        modelBuilder.Entity<Bill>()
            .Property(b => b.SubTotal)
            .HasColumnType("float");

        modelBuilder.Entity<Bill>()
            .Property(b => b.Discount)
            .HasColumnType("float");

        modelBuilder.Entity<Bill>()
            .Property(b => b.Tax)
            .HasColumnType("float");

        modelBuilder.Entity<Bill>()
            .Property(b => b.Total)
            .HasColumnType("float");

        modelBuilder.Entity<BillDetail>()
            .Property(bd => bd.Cost)
            .HasColumnType("float");

        modelBuilder.Entity<BillDetail>()
            .Property(bd => bd.Price)
            .HasColumnType("float");

        modelBuilder.Entity<BillDetail>()
            .Property(bd => bd.Total)
            .HasColumnType("float");

        // Configure BoukakCustomerMapping relationships
        modelBuilder.Entity<BoukakCustomerMapping>()
            .HasOne(bcm => bcm.Customer)
            .WithMany()
            .HasForeignKey(bcm => bcm.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<BoukakCustomerMapping>()
            .HasOne(bcm => bcm.Location)
            .WithMany()
            .HasForeignKey(bcm => bcm.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}



