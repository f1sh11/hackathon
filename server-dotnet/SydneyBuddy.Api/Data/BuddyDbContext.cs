using Microsoft.EntityFrameworkCore;

namespace SydneyBuddy.Api;

/// <summary>定义 PostgreSQL 实体映射与版本约束；owns the EF Core model, JSONB columns and revision concurrency rules.</summary>
public sealed class BuddyDbContext(DbContextOptions<BuddyDbContext> options) : DbContext(options)
{
    public DbSet<TripEntity> Trips => Set<TripEntity>();
    public DbSet<MemoryEntity> Memories => Set<MemoryEntity>();
    public DbSet<TravelWorldEntity> Worlds => Set<TravelWorldEntity>();
    public DbSet<WorldRunEntity> WorldRuns => Set<WorldRunEntity>();
    public DbSet<WorldSnapshotEntity> WorldSnapshots => Set<WorldSnapshotEntity>();
    public DbSet<WorldEventEntity> WorldEvents => Set<WorldEventEntity>();

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

        var worlds = model.Entity<TravelWorldEntity>();
        worlds.ToTable("travel_worlds");
        worlds.HasKey(world => world.Id);
        worlds.Property(world => world.Id).HasColumnName("id").HasColumnType("text").ValueGeneratedNever();
        worlds.Property(world => world.TripId).HasColumnName("trip_id").HasColumnType("text").IsRequired();
        worlds.Property(world => world.SchemaVersion).HasColumnName("schema_version");
        worlds.Property(world => world.CreatedAt).HasColumnName("created_at").HasColumnType("text").IsRequired();
        worlds.HasIndex(world => world.TripId).IsUnique();
        worlds.HasOne<TripEntity>().WithMany().HasForeignKey(world => world.TripId).OnDelete(DeleteBehavior.Cascade);

        var runs = model.Entity<WorldRunEntity>();
        runs.ToTable("world_runs");
        runs.HasKey(run => run.Id);
        runs.Property(run => run.Id).HasColumnName("id").HasColumnType("text").ValueGeneratedNever();
        runs.Property(run => run.WorldId).HasColumnName("world_id").HasColumnType("text").IsRequired();
        runs.Property(run => run.RequestId).HasColumnName("request_id").HasColumnType("text").IsRequired();
        runs.Property(run => run.Kind).HasColumnName("kind").HasColumnType("text").IsRequired();
        runs.Property(run => run.TripRevision).HasColumnName("trip_revision");
        runs.Property(run => run.Status).HasColumnName("status").HasColumnType("text").IsRequired();
        runs.Property(run => run.CreatedAt).HasColumnName("created_at").HasColumnType("text").IsRequired();
        runs.Property(run => run.CompletedAt).HasColumnName("completed_at").HasColumnType("text");
        runs.HasIndex(run => new { run.WorldId, run.RequestId }).IsUnique();
        runs.HasOne<TravelWorldEntity>().WithMany().HasForeignKey(run => run.WorldId).OnDelete(DeleteBehavior.Cascade);

        var snapshots = model.Entity<WorldSnapshotEntity>();
        snapshots.ToTable("world_snapshots");
        snapshots.HasKey(snapshot => snapshot.Id);
        snapshots.Property(snapshot => snapshot.Id).HasColumnName("id").HasColumnType("text").ValueGeneratedNever();
        snapshots.Property(snapshot => snapshot.WorldId).HasColumnName("world_id").HasColumnType("text").IsRequired();
        snapshots.Property(snapshot => snapshot.RunId).HasColumnName("run_id").HasColumnType("text").IsRequired();
        snapshots.Property(snapshot => snapshot.Sequence).HasColumnName("sequence");
        snapshots.Property(snapshot => snapshot.OccurredAt).HasColumnName("occurred_at").HasColumnType("text").IsRequired();
        snapshots.Property(snapshot => snapshot.DataJson).HasColumnName("data").HasColumnType("jsonb").IsRequired();
        snapshots.HasIndex(snapshot => new { snapshot.RunId, snapshot.Sequence }).IsUnique();
        snapshots.HasOne<TravelWorldEntity>().WithMany().HasForeignKey(snapshot => snapshot.WorldId).OnDelete(DeleteBehavior.Cascade);
        snapshots.HasOne<WorldRunEntity>().WithMany().HasForeignKey(snapshot => snapshot.RunId).OnDelete(DeleteBehavior.Cascade);

        var events = model.Entity<WorldEventEntity>();
        events.ToTable("world_events");
        events.HasKey(@event => @event.Id);
        events.Property(@event => @event.Id).HasColumnName("id").HasColumnType("text").ValueGeneratedNever();
        events.Property(@event => @event.WorldId).HasColumnName("world_id").HasColumnType("text").IsRequired();
        events.Property(@event => @event.RunId).HasColumnName("run_id").HasColumnType("text").IsRequired();
        events.Property(@event => @event.Type).HasColumnName("type").HasColumnType("text").IsRequired();
        events.Property(@event => @event.IdempotencyKey).HasColumnName("idempotency_key").HasColumnType("text").IsRequired();
        events.Property(@event => @event.Actor).HasColumnName("actor").HasColumnType("text").IsRequired();
        events.Property(@event => @event.OccurredAt).HasColumnName("occurred_at").HasColumnType("text").IsRequired();
        events.Property(@event => @event.DataJson).HasColumnName("data").HasColumnType("jsonb").IsRequired();
        events.HasIndex(@event => new { @event.WorldId, @event.IdempotencyKey }).IsUnique();
        events.HasOne<TravelWorldEntity>().WithMany().HasForeignKey(@event => @event.WorldId).OnDelete(DeleteBehavior.Cascade);
        events.HasOne<WorldRunEntity>().WithMany().HasForeignKey(@event => @event.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SocialImportEntity { public string Id {get;set;}=""; public string DataJson {get;set;}="{}"; }

public sealed class RestaurantImportEntity {public string Id{get;set;}="";public string VenueId{get;set;}="";public string DataJson{get;set;}="{}";}
public sealed class RestaurantVenueEntity {public string Id{get;set;}="";public string DataJson{get;set;}="{}";}

public sealed class RestaurantEstimateEntity {public string Id{get;set;}="";public string DataJson{get;set;}="{}";}
