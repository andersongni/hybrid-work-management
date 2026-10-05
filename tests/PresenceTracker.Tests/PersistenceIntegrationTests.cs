using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PresenceTracker.Application;
using PresenceTracker.Domain;
using PresenceTracker.Infrastructure;
using PresenceTracker.Persistence;

namespace PresenceTracker.Tests;

public sealed class PersistenceIntegrationTests
{
    [Fact]
    public async Task MigrationSeedsDefaultsAndPersistsNetworkEventsAndBatchChanges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TrackerDbContext>().UseSqlite(connection).Options;
        await using var db = new TrackerDbContext(options);
        var repository = new EfTrackerRepository(db, new LocalHolidayProvider());

        await repository.InitializeAsync();

        var config = await db.Settings.SingleAsync();
        Assert.Equal(40m, config.TargetPercent);
        Assert.Equal(WorkingDays.Weekdays, config.WorkingDays);
        Assert.Equal(10, config.WifiCheckIntervalMinutes);
        Assert.Contains(await db.PresenceNetworks.ToListAsync(), n => n.Ssid == "CORP" && n.IsActive);
        Assert.Contains(await db.Holidays.ToListAsync(), h => h.Date == new DateOnly(2026, 7, 9) && h.Scope == HolidayScope.State);

        var corp = await db.PresenceNetworks.SingleAsync(n => n.Ssid == "CORP");
        corp.CountsAsPresence = true;
        await db.SaveChangesAsync();

        var time = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.FromHours(-3));
        await repository.RegisterNetworkChangeAsync(new NetworkChange(time, "wifi-1", "Wi-Fi", "corp", NetworkEventType.Connected));
        await repository.RegisterNetworkChangeAsync(new NetworkChange(time.AddMinutes(2), "wifi-1", "Wi-Fi", "CORP", NetworkEventType.Connected));
        await repository.RegisterNetworkChangeAsync(new NetworkChange(time.AddHours(1), "wifi-1", "Wi-Fi", "HOME", NetworkEventType.Connected));
        var month = await repository.LoadMonthAsync(2026, 6);
        Assert.Equal(3, month.NetworkEvents.Count);
        // CORP counts as presence; repeated same-day CORP connects do not create a second attendance.
        Assert.Equal(1, month.AttendanceEvents.Count(e => e.Source == AttendanceSource.Automatic));
        Assert.Equal(1, new PresenceCalculator().CalculateMonth(2026, 6, new DateOnly(2026, 6, 1), 40m,
            month.Snapshot.WorkingDays, month.Holidays, month.Classifications, month.AttendanceEvents, month.Plans).RealizedDays);

        await repository.ApplyBatchAsync(
            new[] { new DateOnly(2026, 6, 2), new DateOnly(2026, 6, 3) }, BatchAction.Vacation);
        month = await repository.LoadMonthAsync(2026, 6);
        Assert.Equal(2, month.Classifications.Count(c => c.Type == AbsenceType.Vacation));

        var auto = month.AttendanceEvents.First(e => e.Source == AttendanceSource.Automatic);
        await repository.SetAttendanceStatusAsync(auto.Id, AttendanceStatus.Excluded);
        month = await repository.LoadMonthAsync(2026, 6);
        Assert.Contains(month.AttendanceEvents, e => e.Id == auto.Id && e.Status == AttendanceStatus.Excluded);
    }

    [Fact]
    public async Task ManualHolidayOverridesSynchronizedValueForTheSameDate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TrackerDbContext>().UseSqlite(connection).Options;
        await using var db = new TrackerDbContext(options);
        var repository = new EfTrackerRepository(db, new LocalHolidayProvider());
        await repository.InitializeAsync();

        var date = new DateOnly(2026, 9, 7);
        var original = (await repository.GetHolidaysAsync(2026)).First(h => h.Date == date && h.Scope == HolidayScope.National);
        original.Name = "Independência corrigida";
        await repository.SaveHolidayAsync(original);
        await repository.MergeSynchronizedHolidaysAsync(new[]
        {
            new Holiday { Date = date, Name = "Nome remoto", Scope = HolidayScope.National, Source = HolidaySource.Synchronized }
        });

        var result = (await repository.GetHolidaysAsync(2026)).Where(h => h.Date == date).ToArray();
        Assert.Contains(result, h => h.Name == "Independência corrigida" && h.Source == HolidaySource.Manual);
        Assert.DoesNotContain(result, h => h.Name == "Nome remoto");
    }

    [Fact]
    public async Task UpdateSettingsPersistsWifiCheckInterval()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<TrackerDbContext>().UseSqlite(connection).Options;
        await using var db = new TrackerDbContext(options);
        var repository = new EfTrackerRepository(db, new LocalHolidayProvider());
        await repository.InitializeAsync();

        var settings = await db.Settings.AsNoTracking().SingleAsync();
        settings.WifiCheckIntervalMinutes = 15;
        var networks = await db.PresenceNetworks.AsNoTracking().ToListAsync();
        await repository.UpdateSettingsAsync(settings, networks);

        db.ChangeTracker.Clear();
        Assert.Equal(15, (await db.Settings.SingleAsync()).WifiCheckIntervalMinutes);
    }
}

