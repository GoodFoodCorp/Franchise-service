using System.Text.Json;
using Franchise.Domain.Entities;
using Franchise.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Franchise.Infrastructure.Persistence;

public sealed class FranchiseDbContext(DbContextOptions<FranchiseDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.TimeZone).HasMaxLength(64).HasDefaultValue(Restaurant.DefaultTimeZone);
            e.Property(x => x.AcceptingOrders).HasDefaultValue(true);
            e.Property(x => x.PrepTimeMinutes).HasDefaultValue(20);
            e.Property(x => x.DeliveryRadiusKm).HasDefaultValue(5.0);
            e.Property(x => x.DeliveryFeeCents).HasDefaultValue(299L);
            e.Property(x => x.MinimumOrderCents).HasDefaultValue(0L);
            e.Property(x => x.SeatingCapacity).HasDefaultValue(40);
            e.Ignore(x => x.Location);
            // The weekly schedule is a small value list read with its
            // restaurant: stored as one jsonb column.
            e.Property<List<OpeningSlot>>("_openingHours")
                .HasColumnName("OpeningHours")
                .HasColumnType("jsonb")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => JsonSerializer.Deserialize<List<OpeningSlot>>(v, JsonOptions) ?? new List<OpeningSlot>(),
                    new ValueComparer<List<OpeningSlot>>(
                        (a, b) => a!.SequenceEqual(b!),
                        v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                        v => v.ToList()))
                .HasDefaultValueSql("'[]'::jsonb");
            e.Ignore(x => x.OpeningHours);
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
