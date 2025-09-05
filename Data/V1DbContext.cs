using Microsoft.EntityFrameworkCore;
using Karage.Functions.Models;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure relationships
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
    }
}