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

public sealed record BatchApplyResult(int AppliedCount, IReadOnlyList<DateOnly> SkippedPastPlanDates)
{
    public bool HasSkippedPastPlans => SkippedPastPlanDates.Count > 0;

    public static string FormatPastPlanSkippedMessage(int skippedCount, int appliedCount)
    {
        if (skippedCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(skippedCount));

        if (appliedCount == 0)
        {
            return skippedCount == 1
                ? "Não é possível planejar um dia anterior a hoje. O planejamento vale apenas para hoje e datas futuras."
                : $"Não é possível planejar {skippedCount} dias anteriores a hoje. O planejamento vale apenas para hoje e datas futuras.";
        }

        return skippedCount == 1
            ? "1 dia no passado foi ignorado porque o planejamento vale apenas para hoje e datas futuras."
            : $"{skippedCount} dias no passado foram ignorados porque o planejamento vale apenas para hoje e datas futuras.";
    }

    public string PastPlanSkippedMessage => FormatPastPlanSkippedMessage(SkippedPastPlanDates.Count, AppliedCount);
}

public enum BatchAction
{
    ManualPresence,
    PlanPresence,
    Vacation,
    DayOff,
    Holiday,
    NonWorkingDay,
    RestoreDefaults
}

public interface ITrackerRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<TrackerMonthData> LoadMonthAsync(int year, int month, CancellationToken cancellationToken = default);
    Task ApplyBatchAsync(IReadOnlyCollection<DateOnly> dates, BatchAction action, CancellationToken cancellationToken = default);
    Task SetAttendanceStatusAsync(long attendanceId, AttendanceStatus status, CancellationToken cancellationToken = default);
    Task<TrackerSettings> UpdateSettingsAsync(TrackerSettings settings, IReadOnlyCollection<PresenceNetwork> networks,
        CancellationToken cancellationToken = default, IReadOnlyCollection<int>? removedNetworkIds = null);
    Task RefreshAfterRestoreAsync(CancellationToken cancellationToken = default);
    Task ResetToFactoryDefaultsAsync(CancellationToken cancellationToken = default);
    Task RegisterNetworkChangeAsync(NetworkChange change, CancellationToken cancellationToken = default);
    Task ClearPlansBeforeAsync(DateOnly today, CancellationToken cancellationToken = default);
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
        await repository.ClearPlansBeforeAsync(today, cancellationToken);
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
            var planned = date >= today && data.Plans.Any(p => p.Date == date);
            var outcome = calculator.EvaluateDay(date, data.Snapshot.WorkingDays, holiday,
                classification, attendance, planned);
            days.Add(new DayViewData(outcome, attendance, networkEvents));
        }
        return new MonthViewData(data, metrics, days);
    }

    public async Task<BatchApplyResult> ApplyBatchAsync(IReadOnlyCollection<DateOnly> dates, BatchAction action,
        CancellationToken cancellationToken = default, DateOnly? today = null)
    {
        var distinct = dates.Distinct().OrderBy(date => date).ToArray();
        IReadOnlyList<DateOnly> skippedPastPlans = [];
        var toApply = distinct;

        if (action == BatchAction.PlanPresence)
        {
            var currentDay = today ?? DateOnly.FromDateTime(DateTime.Today);
            skippedPastPlans = distinct.Where(date => date < currentDay).ToArray();
            toApply = distinct.Where(date => date >= currentDay).ToArray();
        }

        if (toApply.Length > 0)
            await repository.ApplyBatchAsync(toApply, action, cancellationToken);

        return new BatchApplyResult(toApply.Length, skippedPastPlans);
    }

    public Task SetAttendanceStatusAsync(long id, AttendanceStatus status, CancellationToken cancellationToken = default) =>
        repository.SetAttendanceStatusAsync(id, status, cancellationToken);

    public Task<TrackerSettings> UpdateSettingsAsync(TrackerSettings settings, IReadOnlyCollection<PresenceNetwork> networks,
        CancellationToken cancellationToken = default, IReadOnlyCollection<int>? removedNetworkIds = null)
    {
        PresenceCalculator.ValidateTarget(settings.TargetPercent);
        if (networks.Any(n => string.IsNullOrWhiteSpace(n.Ssid) || System.Text.Encoding.UTF8.GetByteCount(n.Ssid.Trim()) > 32))
            throw new ArgumentException("Cada SSID deve conter de 1 a 32 bytes.", nameof(networks));
        if (networks.Select(n => n.Ssid.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != networks.Count)
            throw new ArgumentException("Os SSIDs devem ser únicos.", nameof(networks));
        if (settings.BackupRetentionCount is < 1 or > 365)
            throw new ArgumentOutOfRangeException(nameof(settings.BackupRetentionCount), "A retenção deve ficar entre 1 e 365 backups.");
        if (settings.BackupIntervalHours is < 1 or > 720)
            throw new ArgumentOutOfRangeException(nameof(settings.BackupIntervalHours), "O intervalo deve ficar entre 1 e 720 horas.");
        if (settings.WifiCheckIntervalMinutes is < 1 or > 1440)
            throw new ArgumentOutOfRangeException(nameof(settings.WifiCheckIntervalMinutes), "O intervalo de verificação Wi-Fi deve ficar entre 1 e 1440 minutos.");
        return repository.UpdateSettingsAsync(settings, networks, cancellationToken, removedNetworkIds);
    }

    public Task RefreshAfterRestoreAsync(CancellationToken cancellationToken = default) =>
        repository.RefreshAfterRestoreAsync(cancellationToken);

    public Task ResetToFactoryDefaultsAsync(CancellationToken cancellationToken = default) =>
        repository.ResetToFactoryDefaultsAsync(cancellationToken);

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










