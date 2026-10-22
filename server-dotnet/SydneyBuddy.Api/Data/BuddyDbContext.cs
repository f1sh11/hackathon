using Microsoft.EntityFrameworkCore;

namespace SydneyBuddy.Api;

/// <summary>定义 PostgreSQL 实体映射与版本约束；owns the EF Core model, JSONB columns and revision concurrency rules.</summary>
public sealed class BuddyDbContext(DbContextOptions<BuddyDbContext> options) : DbContext(options)
{
    public DbSet<TripEntity> Trips => Set<TripEntity>();
    public DbSet<MemoryEntity> Memories => Set<MemoryEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var trips = model.Entity<TripEntity>();
        // 不指定 schema；respect the connection SearchPath, including isolated integration-test schemas.
        trips.ToTable("trips", table => table.HasCheckConstraint("ck_trips_revision", "revision > 0"));
        trips.HasKey(trip => trip.Id);
        trips.Property(trip => trip.Id).HasColumnName("id").HasColumnType("text").ValueGeneratedNever();
        trips.Property(trip => trip.DataJson).HasColumnName("data").HasColumnType("jsonb").IsRequired();
        trips.Property(trip => trip.Revision).HasColumnName("revision").IsConcurrencyToken();

        var memories = model.Entity<MemoryEntity>();
        memories.ToTable("memories");
        memories.HasKey(memory => memory.Id);
        memories.Property(memory => memory.Id).HasColumnName("id").HasColumnType("text").ValueGeneratedNever();
        memories.Property(memory => memory.DataJson).HasColumnName("data").HasColumnType("jsonb").IsRequired();
    }
}
