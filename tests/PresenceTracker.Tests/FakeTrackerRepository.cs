using PresenceTracker.Application;
using PresenceTracker.Domain;

namespace PresenceTracker.Tests;

internal sealed class FakeTrackerRepository : ITrackerRepository
{
    public TrackerSettings Settings { get; set; } = new();
    public List<PresenceNetwork> Networks { get; } = [];
    public List<Holiday> Holidays { get; } = [];
    public List<DayClassification> Classifications { get; } = [];
    public List<AttendanceEvent> AttendanceEvents { get; } = [];
    public List<NetworkEvent> NetworkEvents { get; } = [];
    public List<PresencePlan> Plans { get; } = [];
    public MonthlySnapshot Snapshot { get; set; } = new()
    {
        Year = 2026,
        Month = 6,
        TargetPercent = 40m,
        WorkingDays = WorkingDays.Weekdays
    };

    public int ClearPlansBeforeCalls { get; private set; }
    public DateOnly? LastClearPlansBefore { get; private set; }
    public IReadOnlyList<Holiday>? LastMergedHolidays { get; private set; }
    public IReadOnlyCollection<DateOnly>? LastBatchDates { get; private set; }
    public BatchAction? LastBatchAction { get; private set; }
    public TrackerSettings? LastUpdatedSettings { get; private set; }
    public IReadOnlyCollection<PresenceNetwork>? LastUpdatedNetworks { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<TrackerMonthData> LoadMonthAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        Snapshot.Year = year;
        Snapshot.Month = month;
        Snapshot.TargetPercent = Settings.TargetPercent;
        Snapshot.WorkingDays = Settings.WorkingDays;
        return Task.FromResult(new TrackerMonthData(
            Settings, Snapshot, Networks, Holidays, Classifications, AttendanceEvents, NetworkEvents, Plans));
    }

    public Task ApplyBatchAsync(IReadOnlyCollection<DateOnly> dates, BatchAction action,
        CancellationToken cancellationToken = default)
    {
        LastBatchDates = dates.ToArray();
        LastBatchAction = action;
        return Task.CompletedTask;
    }

    public Task SetAttendanceStatusAsync(long attendanceId, AttendanceStatus status,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<TrackerSettings> UpdateSettingsAsync(TrackerSettings settings,
        IReadOnlyCollection<PresenceNetwork> networks, CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? removedNetworkIds = null)
    {
        LastUpdatedSettings = settings;
        LastUpdatedNetworks = networks.ToArray();
        Settings = settings;
        Networks.Clear();
        Networks.AddRange(networks);
        return Task.FromResult(settings);
    }

    public Task RefreshAfterRestoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ResetToFactoryDefaultsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RegisterNetworkChangeAsync(NetworkChange change, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ClearPlansBeforeAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        ClearPlansBeforeCalls++;
        LastClearPlansBefore = today;
        Plans.RemoveAll(plan => plan.Date < today);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Holiday>> GetHolidaysAsync(int year, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Holiday>>(Holidays.Where(h => h.Date.Year == year).ToArray());

    public Task SaveHolidayAsync(Holiday holiday, CancellationToken cancellationToken = default)
    {
        Holidays.RemoveAll(h => h.Id == holiday.Id && holiday.Id != 0);
        Holidays.Add(holiday);
        return Task.CompletedTask;
    }

    public Task DeleteHolidayAsync(int holidayId, CancellationToken cancellationToken = default)
    {
        Holidays.RemoveAll(h => h.Id == holidayId);
        return Task.CompletedTask;
    }

    public Task MergeSynchronizedHolidaysAsync(IEnumerable<Holiday> holidays,
        CancellationToken cancellationToken = default)
    {
        LastMergedHolidays = holidays.ToArray();
        return Task.CompletedTask;
    }
}

internal sealed class FakeHolidayProvider(IReadOnlyList<Holiday> holidays) : IHolidayProvider
{
    public Task<IReadOnlyList<Holiday>> GetHolidaysAsync(int year, CancellationToken cancellationToken = default) =>
        Task.FromResult(holidays);
}
