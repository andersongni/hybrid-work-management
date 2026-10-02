using PresenceTracker.Domain;

namespace PresenceTracker.Application;

public sealed record NetworkChange(DateTimeOffset OccurredAt, string InterfaceId, string InterfaceName,
    string? Ssid, NetworkEventType Type);

public sealed record TrackerMonthData(TrackerSettings Settings, MonthlySnapshot Snapshot,
    IReadOnlyList<PresenceNetwork> Networks, IReadOnlyList<Holiday> Holidays,
    IReadOnlyList<DayClassification> Classifications, IReadOnlyList<AttendanceEvent> AttendanceEvents,
    IReadOnlyList<NetworkEvent> NetworkEvents, IReadOnlyList<PresencePlan> Plans);

public sealed record DayViewData(DayOutcome Outcome, IReadOnlyList<AttendanceEvent> AttendanceEvents,
    IReadOnlyList<NetworkEvent> NetworkEvents);

public sealed record MonthViewData(TrackerMonthData Data, MonthMetrics Metrics, IReadOnlyList<DayViewData> Days);

public enum BatchAction
{
    ManualPresence,
    PlanPresence,
    Vacation,
    DayOff,
    Holiday,
    NonWorkingDay,
    RemovePlan,
    RemoveClassification,
    RemoveManualPresence
}

public interface ITrackerRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<TrackerMonthData> LoadMonthAsync(int year, int month, CancellationToken cancellationToken = default);
    Task ApplyBatchAsync(IReadOnlyCollection<DateOnly> dates, BatchAction action, CancellationToken cancellationToken = default);
    Task SetAttendanceStatusAsync(long attendanceId, AttendanceStatus status, CancellationToken cancellationToken = default);
    Task<TrackerSettings> UpdateSettingsAsync(TrackerSettings settings, IReadOnlyCollection<PresenceNetwork> networks, CancellationToken cancellationToken = default);
    Task RegisterNetworkChangeAsync(NetworkChange change, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Holiday>> GetHolidaysAsync(int year, CancellationToken cancellationToken = default);
    Task SaveHolidayAsync(Holiday holiday, CancellationToken cancellationToken = default);
    Task DeleteHolidayAsync(int holidayId, CancellationToken cancellationToken = default);
    Task MergeSynchronizedHolidaysAsync(IEnumerable<Holiday> holidays, CancellationToken cancellationToken = default);
}

public interface IHolidayProvider
{
    Task<IReadOnlyList<Holiday>> GetHolidaysAsync(int year, CancellationToken cancellationToken = default);
}

public sealed class TrackerService(ITrackerRepository repository, PresenceCalculator calculator)
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) => repository.InitializeAsync(cancellationToken);

    public async Task<MonthViewData> GetMonthAsync(int year, int month, DateOnly today, CancellationToken cancellationToken = default)
    {
        var data = await repository.LoadMonthAsync(year, month, cancellationToken);
        var metrics = calculator.CalculateMonth(year, month, today, data.Snapshot.TargetPercent,
            data.Snapshot.WorkingDays, data.Holidays, data.Classifications, data.AttendanceEvents, data.Plans);
        var days = new List<DayViewData>();
        var count = DateTime.DaysInMonth(year, month);
        for (var i = 1; i <= count; i++)
        {
            var date = new DateOnly(year, month, i);
            var holiday = data.Holidays.Where(h => h.Date == date && h.IsActive).OrderByDescending(h => h.Source == HolidaySource.Manual).FirstOrDefault();
            var classification = data.Classifications.FirstOrDefault(c => c.Date == date);
            var attendance = data.AttendanceEvents.Where(e => e.Date == date).ToArray();
            var networkEvents = data.NetworkEvents.Where(e => e.Date == date).OrderBy(e => e.OccurredAt).ToArray();
            var planned = data.Plans.Any(p => p.Date == date);
            var outcome = calculator.EvaluateDay(date, data.Snapshot.WorkingDays, holiday,
                classification, attendance, planned);
            days.Add(new DayViewData(outcome, attendance, networkEvents));
        }
        return new MonthViewData(data, metrics, days);
    }

    public Task ApplyBatchAsync(IReadOnlyCollection<DateOnly> dates, BatchAction action, CancellationToken cancellationToken = default) =>
        repository.ApplyBatchAsync(dates.Distinct().ToArray(), action, cancellationToken);

    public Task SetAttendanceStatusAsync(long id, AttendanceStatus status, CancellationToken cancellationToken = default) =>
        repository.SetAttendanceStatusAsync(id, status, cancellationToken);

    public Task<TrackerSettings> UpdateSettingsAsync(TrackerSettings settings, IReadOnlyCollection<PresenceNetwork> networks, CancellationToken cancellationToken = default)
    {
        PresenceCalculator.ValidateTarget(settings.TargetPercent);
        if (networks.Any(n => string.IsNullOrWhiteSpace(n.Ssid) || System.Text.Encoding.UTF8.GetByteCount(n.Ssid.Trim()) > 32))
            throw new ArgumentException("Cada SSID deve conter de 1 a 32 bytes.", nameof(networks));
        if (networks.Select(n => n.Ssid.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != networks.Count)
            throw new ArgumentException("Os SSIDs devem ser únicos.", nameof(networks));
        if (settings.BackupRetentionCount is < 1 or > 365)
            throw new ArgumentOutOfRangeException(nameof(settings.BackupRetentionCount), "A retenção deve ficar entre 1 e 365 backups.");
        return repository.UpdateSettingsAsync(settings, networks, cancellationToken);
    }

    public Task RegisterNetworkChangeAsync(NetworkChange change, CancellationToken cancellationToken = default) =>
        repository.RegisterNetworkChangeAsync(change, cancellationToken);

    public Task<IReadOnlyList<Holiday>> GetHolidaysAsync(int year, CancellationToken cancellationToken = default) =>
        repository.GetHolidaysAsync(year, cancellationToken);

    public Task SaveHolidayAsync(Holiday holiday, CancellationToken cancellationToken = default) =>
        repository.SaveHolidayAsync(holiday, cancellationToken);

    public Task DeleteHolidayAsync(int id, CancellationToken cancellationToken = default) =>
        repository.DeleteHolidayAsync(id, cancellationToken);

    public async Task SynchronizeHolidaysAsync(int year, IHolidayProvider provider, CancellationToken cancellationToken = default)
    {
        var remote = await provider.GetHolidaysAsync(year, cancellationToken);
        await repository.MergeSynchronizedHolidaysAsync(remote, cancellationToken);
    }
}










