using Microsoft.EntityFrameworkCore;

namespace SydneyBuddy.Api;

/// <summary>定义 PostgreSQL 实体映射与版本约束；owns the EF Core model, JSONB columns and revision concurrency rules.</summary>
public sealed class BuddyDbContext(DbContextOptions<BuddyDbContext> options) : DbContext(options)
{
    public DbSet<TripEntity> Trips => Set<TripEntity>();
    public DbSet<MemoryEntity> Memories => Set<MemoryEntity>();

    public DbSet<SocialImportEntity> SocialImports => Set<SocialImportEntity>();

    public DbSet<RestaurantImportEntity> RestaurantImports => Set<RestaurantImportEntity>();
    public DbSet<RestaurantVenueEntity> RestaurantVenues => Set<RestaurantVenueEntity>();
    public DbSet<RestaurantEstimateEntity> RestaurantEstimates => Set<RestaurantEstimateEntity>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        var estimates=model.Entity<RestaurantEstimateEntity>();estimates.ToTable("restaurant_estimates");estimates.HasKey(x=>x.Id);estimates.Property(x=>x.Id).HasColumnName("id").HasColumnType("text").ValueGeneratedNever();estimates.Property(x=>x.DataJson).HasColumnName("data").HasColumnType("jsonb").IsRequired();
        var imports=model.Entity<RestaurantImportEntity>();imports.ToTable("restaurant_imports");imports.HasKey(x=>x.Id);
        imports.Property(x=>x.Id).HasColumnName("id").HasColumnType("text").ValueGeneratedNever();imports.Property(x=>x.VenueId).HasColumnName("venue_id").HasColumnType("text").IsRequired();imports.HasIndex(x=>x.VenueId);
        imports.Property(x=>x.DataJson).HasColumnName("data").HasColumnType("jsonb").IsRequired();
        var venues=model.Entity<RestaurantVenueEntity>();venues.ToTable("restaurant_venues");venues.HasKey(x=>x.Id);venues.Property(x=>x.Id).HasColumnName("id").HasColumnType("text").ValueGeneratedNever();venues.Property(x=>x.DataJson).HasColumnName("data").HasColumnType("jsonb").IsRequired();
        var social=model.Entity<SocialImportEntity>();
        social.ToTable("social_imports");social.HasKey(x=>x.Id);
        social.Property(x=>x.Id).HasColumnName("id").HasColumnType("text").ValueGeneratedNever();
        social.Property(x=>x.DataJson).HasColumnName("data").HasColumnType("jsonb").IsRequired();
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

public sealed class SocialImportEntity { public string Id {get;set;}=""; public string DataJson {get;set;}="{}"; }

public sealed class RestaurantImportEntity {public string Id{get;set;}="";public string VenueId{get;set;}="";public string DataJson{get;set;}="{}";}
public sealed class RestaurantVenueEntity {public string Id{get;set;}="";public string DataJson{get;set;}="{}";}

public sealed class RestaurantEstimateEntity {public string Id{get;set;}="";public string DataJson{get;set;}="{}";}
