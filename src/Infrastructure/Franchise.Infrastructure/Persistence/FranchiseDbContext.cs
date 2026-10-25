using Franchise.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Franchise.Infrastructure.Persistence;

public sealed class FranchiseDbContext(DbContextOptions<FranchiseDbContext> options) : DbContext(options)
{
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Restaurant>(e =>
        {
            e.ToTable("restaurants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(200).IsRequired();
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.City).HasMaxLength(120);
            e.Property(x => x.Plan).HasMaxLength(50);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasMany(x => x.Suppliers)
                .WithOne()
                .HasForeignKey(s => s.RestaurantId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Suppliers).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Supplier>(e =>
        {
            e.ToTable("suppliers");
            e.HasKey(x => x.Id);
            // The id is assigned by the domain (Guid.NewGuid()); without this EF treats a
            // supplier added through Restaurant.Suppliers as existing and issues an UPDATE.
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.ContactName).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.HasIndex(x => x.RestaurantId);
        });
    }
}
