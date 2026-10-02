using Microsoft.EntityFrameworkCore;
using PresenceTracker.Domain;

namespace PresenceTracker.Persistence;

public sealed class TrackerDbContext(DbContextOptions<TrackerDbContext> options) : DbContext(options)
{
    public DbSet<TrackerSettings> Settings => Set<TrackerSettings>();
    public DbSet<PresenceNetwork> PresenceNetworks => Set<PresenceNetwork>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<DayClassification> Classifications => Set<DayClassification>();
    public DbSet<AttendanceEvent> AttendanceEvents => Set<AttendanceEvent>();
    public DbSet<NetworkEvent> NetworkEvents => Set<NetworkEvent>();
    public DbSet<PresencePlan> Plans => Set<PresencePlan>();
    public DbSet<MonthlySnapshot> MonthlySnapshots => Set<MonthlySnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrackerSettings>().HasKey(x => x.Id);
        modelBuilder.Entity<PresenceNetwork>().HasIndex(x => x.Ssid).IsUnique();
        modelBuilder.Entity<Holiday>().HasIndex(x => new { x.Date, x.Scope, x.Name }).IsUnique();
        modelBuilder.Entity<DayClassification>().HasIndex(x => x.Date).IsUnique();
        modelBuilder.Entity<AttendanceEvent>().HasIndex(x => new { x.Date, x.Source });
        modelBuilder.Entity<NetworkEvent>().HasIndex(x => new { x.Date, x.OccurredAt });
        modelBuilder.Entity<PresencePlan>().HasIndex(x => x.Date).IsUnique();
        modelBuilder.Entity<MonthlySnapshot>().HasIndex(x => new { x.Year, x.Month }).IsUnique();
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.ClrType.IsEnum)
                continue;
        }
    }
}





