using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PresenceTracker.Application;
using PresenceTracker.Domain;

namespace PresenceTracker.Persistence;

public sealed class EfTrackerRepository(TrackerDbContext db, IHolidayProvider localHolidays) : ITrackerRepository
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);
        if (!await db.Settings.AnyAsync(cancellationToken))
        {
            db.Settings.Add(new TrackerSettings());
            db.PresenceNetworks.Add(new PresenceNetwork { Ssid = "CORP", IsActive = true, CountsAsPresence = true });
            await db.SaveChangesAsync(cancellationToken);
        }
        if (!await db.Holidays.AnyAsync(cancellationToken))
        {
            foreach (var year in new[] { 2026, 2027 })
                db.Holidays.AddRange(await localHolidays.GetHolidaysAsync(year, cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
        }
        await EnsureSnapshotAsync(DateOnly.FromDateTime(DateTime.Now), cancellationToken);
    }

    public async Task<TrackerMonthData> LoadMonthAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var monthDate = new DateOnly(year, month, 1);
        var snapshot = await EnsureSnapshotAsync(monthDate, cancellationToken);
        var end = monthDate.AddMonths(1);
        var start = monthDate;
        var settings = await db.Settings.AsNoTracking().FirstAsync(cancellationToken);
        return new TrackerMonthData(settings, snapshot,
            await db.PresenceNetworks.AsNoTracking().OrderBy(x => x.Ssid).ToListAsync(cancellationToken),
            await db.Holidays.AsNoTracking().Where(x => x.Date >= start && x.Date < end).ToListAsync(cancellationToken),
            await db.Classifications.AsNoTracking().Where(x => x.Date >= start && x.Date < end).ToListAsync(cancellationToken),
            await db.AttendanceEvents.AsNoTracking().Where(x => x.Date >= start && x.Date < end).ToListAsync(cancellationToken),
            await db.NetworkEvents.AsNoTracking().Where(x => x.Date >= start && x.Date < end).ToListAsync(cancellationToken),
            await db.Plans.AsNoTracking().Where(x => x.Date >= start && x.Date < end).ToListAsync(cancellationToken));
    }

    public async Task ApplyBatchAsync(IReadOnlyCollection<DateOnly> dates, BatchAction action, CancellationToken cancellationToken = default)
    {
        if (dates.Count == 0) return;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var date in dates.Distinct())
        {
            var plan = await db.Plans.SingleOrDefaultAsync(x => x.Date == date, cancellationToken);
            var classification = await db.Classifications.SingleOrDefaultAsync(x => x.Date == date, cancellationToken);
            switch (action)
            {
                case BatchAction.ManualPresence:
                    if (!await db.AttendanceEvents.AnyAsync(x => x.Date == date && x.Source == AttendanceSource.Manual && x.Status == AttendanceStatus.Active, cancellationToken))
                        db.AttendanceEvents.Add(new AttendanceEvent { Date = date, OccurredAt = DateTimeOffset.Now, Source = AttendanceSource.Manual });
                    if (plan is not null) db.Plans.Remove(plan);
                    break;
                case BatchAction.PlanPresence:
                    if (classification is null && plan is null)
                        db.Plans.Add(new PresencePlan { Date = date, CreatedAt = DateTimeOffset.Now });
                    break;
                case BatchAction.RemovePlan:
                    if (plan is not null) db.Plans.Remove(plan);
                    break;
                case BatchAction.RemoveClassification:
                    if (classification is not null) db.Classifications.Remove(classification);
                    break;
                case BatchAction.RemoveManualPresence:
                {
                    var manualEvents = await db.AttendanceEvents.Where(x => x.Date == date && x.Source == AttendanceSource.Manual).ToListAsync(cancellationToken);
                    db.AttendanceEvents.RemoveRange(manualEvents);
                    break;
                }                default:
                    var type = action switch
                    {
                        BatchAction.Vacation => AbsenceType.Vacation,
                        BatchAction.DayOff => AbsenceType.DayOff,
                        BatchAction.Holiday => AbsenceType.Holiday,
                        _ => AbsenceType.NonWorkingDay
                    };
                    if (classification is null)
                        db.Classifications.Add(new DayClassification { Date = date, Type = type, UpdatedAt = DateTimeOffset.Now });
                    else
                    {
                        classification.Type = type;
                        classification.UpdatedAt = DateTimeOffset.Now;
                    }
                    if (plan is not null) db.Plans.Remove(plan);
                    break;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetAttendanceStatusAsync(long attendanceId, AttendanceStatus status, CancellationToken cancellationToken = default)
    {
        var attendance = await db.AttendanceEvents.FirstOrDefaultAsync(x => x.Id == attendanceId, cancellationToken)
            ?? throw new KeyNotFoundException("Presença não encontrada.");
        if (attendance.Source != AttendanceSource.Automatic)
            throw new InvalidOperationException("Somente presenças automáticas podem ser excluídas pelo histórico.");
        attendance.Status = status;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TrackerSettings> UpdateSettingsAsync(TrackerSettings settings, IReadOnlyCollection<PresenceNetwork> networks, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var current = await db.Settings.FirstAsync(cancellationToken);
        current.TargetPercent = settings.TargetPercent;
        current.WorkingDays = settings.WorkingDays;
        current.StartWithWindows = settings.StartWithWindows;
        current.MinimizeToTray = settings.MinimizeToTray;
        current.Theme = settings.Theme;
        current.BackupRetentionCount = settings.BackupRetentionCount;
        current.MinimumLogLevel = settings.MinimumLogLevel;
        var existing = await db.PresenceNetworks.ToListAsync(cancellationToken);
        var incomingIds = networks.Where(n => n.Id != 0).Select(n => n.Id).ToHashSet();
        foreach (var network in networks.Where(n => n.Id == 0))
        {
            var match = existing.FirstOrDefault(n => string.Equals(n.Ssid, network.Ssid.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                incomingIds.Add(match.Id);
        }
        db.PresenceNetworks.RemoveRange(existing.Where(n => !incomingIds.Contains(n.Id)));
        var addedNetworks = new List<(PresenceNetwork Input, PresenceNetwork Entity)>();
        foreach (var network in networks)
        {
            if (network.Id == 0)
            {
                var item = existing.FirstOrDefault(n => string.Equals(n.Ssid, network.Ssid.Trim(), StringComparison.OrdinalIgnoreCase));
                if (item is null)
                {
                    item = new PresenceNetwork { Ssid = network.Ssid.Trim(), IsActive = network.IsActive, CountsAsPresence = network.CountsAsPresence };
                    db.PresenceNetworks.Add(item);
                    addedNetworks.Add((network, item));
                }
                else
                {
                    item.Ssid = network.Ssid.Trim();
                    item.IsActive = network.IsActive;
                    item.CountsAsPresence = network.CountsAsPresence;
                    network.Id = item.Id;
                }
            }
            else
            {
                var item = existing.First(n => n.Id == network.Id);
                item.Ssid = network.Ssid.Trim();
                item.IsActive = network.IsActive;
                item.CountsAsPresence = network.CountsAsPresence;
            }
        }
        var now = DateTime.Now;
        var snapshot = await db.MonthlySnapshots.SingleOrDefaultAsync(x => x.Year == now.Year && x.Month == now.Month, cancellationToken);
        if (snapshot is null)
            db.MonthlySnapshots.Add(new MonthlySnapshot { Year = now.Year, Month = now.Month, TargetPercent = settings.TargetPercent, WorkingDays = settings.WorkingDays, CreatedAt = DateTimeOffset.Now, UpdatedAt = DateTimeOffset.Now });
        else
        {
            snapshot.TargetPercent = settings.TargetPercent;
            snapshot.WorkingDays = settings.WorkingDays;
            snapshot.UpdatedAt = DateTimeOffset.Now;
        }
        await db.SaveChangesAsync(cancellationToken);
        foreach (var (input, entity) in addedNetworks)
            input.Id = entity.Id;
        await transaction.CommitAsync(cancellationToken);
        return current;
    }

    public async Task RegisterNetworkChangeAsync(NetworkChange change, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var localDate = DateOnly.FromDateTime(change.OccurredAt.LocalDateTime);
        var networkEvent = new NetworkEvent
        {
            Date = localDate, OccurredAt = change.OccurredAt, Ssid = change.Ssid,
            InterfaceId = change.InterfaceId, InterfaceName = change.InterfaceName, Type = change.Type
        };
        db.NetworkEvents.Add(networkEvent);
        await db.SaveChangesAsync(cancellationToken);
        if (change.Type == NetworkEventType.Connected && !string.IsNullOrWhiteSpace(change.Ssid))
        {
            var configuredNetworks = await db.PresenceNetworks.AsNoTracking().Where(x => x.IsActive && x.CountsAsPresence).ToListAsync(cancellationToken);
            var qualifies = configuredNetworks.Any(x => string.Equals(x.Ssid, change.Ssid, StringComparison.OrdinalIgnoreCase));
            if (qualifies)
            {
                var alreadyResolvedForDay = await db.AttendanceEvents.AsNoTracking().AnyAsync(attendance =>
                    attendance.Date == localDate &&
                    (attendance.Source == AttendanceSource.Automatic || attendance.Status == AttendanceStatus.Active), cancellationToken);
                if (!alreadyResolvedForDay)
                    db.AttendanceEvents.Add(new AttendanceEvent { Date = localDate, OccurredAt = change.OccurredAt, Source = AttendanceSource.Automatic, Status = AttendanceStatus.Active, NetworkEventId = networkEvent.Id });
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Holiday>> GetHolidaysAsync(int year, CancellationToken cancellationToken = default) =>
        await db.Holidays.AsNoTracking().Where(x => x.Date.Year == year).OrderBy(x => x.Date).ToListAsync(cancellationToken);

    public async Task SaveHolidayAsync(Holiday holiday, CancellationToken cancellationToken = default)
    {
        var existing = holiday.Id == 0
            ? await db.Holidays.Where(x => x.Date == holiday.Date && x.Scope == holiday.Scope)
                .OrderByDescending(x => x.Source == HolidaySource.Manual).FirstOrDefaultAsync(cancellationToken)
            : await db.Holidays.FindAsync([holiday.Id], cancellationToken);
        if (existing is null)
        {
            holiday.Source = HolidaySource.Manual;
            holiday.CreatedAt = DateTimeOffset.Now;
            holiday.UpdatedAt = DateTimeOffset.Now;
            db.Holidays.Add(holiday);
        }
        else
        {
            holiday.Id = existing.Id;
            existing.Date = holiday.Date;
            existing.Name = holiday.Name.Trim();
            existing.Scope = holiday.Scope;
            existing.IsActive = holiday.IsActive;
            existing.Source = HolidaySource.Manual;
            existing.UpdatedAt = DateTimeOffset.Now;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteHolidayAsync(int holidayId, CancellationToken cancellationToken = default)
    {
        var item = await db.Holidays.FindAsync([holidayId], cancellationToken);
        if (item is not null) { db.Holidays.Remove(item); await db.SaveChangesAsync(cancellationToken); }
    }

    public async Task MergeSynchronizedHolidaysAsync(IEnumerable<Holiday> holidays, CancellationToken cancellationToken = default)
    {
        foreach (var incoming in holidays)
        {
            var sameDate = await db.Holidays.Where(x => x.Date == incoming.Date).ToListAsync(cancellationToken);
            if (sameDate.Any(x => x.Source == HolidaySource.Manual))
                continue;
            var sameScope = sameDate.Where(x => x.Scope == incoming.Scope).ToList();            var existing = sameScope.FirstOrDefault();
            if (existing is null)
            {
                incoming.CreatedAt = incoming.UpdatedAt = DateTimeOffset.Now;
                db.Holidays.Add(incoming);
            }
            else
            {
                existing.Name = incoming.Name;
                existing.Source = incoming.Source;
                existing.IsActive = true;
                existing.UpdatedAt = DateTimeOffset.Now;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<MonthlySnapshot> EnsureSnapshotAsync(DateOnly month, CancellationToken cancellationToken)
    {
        var snapshot = await db.MonthlySnapshots.SingleOrDefaultAsync(x => x.Year == month.Year && x.Month == month.Month, cancellationToken);
        if (snapshot is not null) return snapshot;
        var settings = await db.Settings.FirstOrDefaultAsync(cancellationToken);
        var target = settings?.TargetPercent ?? 40m;
        var days = settings?.WorkingDays ?? WorkingDays.Weekdays;
        snapshot = new MonthlySnapshot { Year = month.Year, Month = month.Month, TargetPercent = target, WorkingDays = days, CreatedAt = DateTimeOffset.Now, UpdatedAt = DateTimeOffset.Now };
        db.MonthlySnapshots.Add(snapshot);
        await db.SaveChangesAsync(cancellationToken);
        return snapshot;
    }
}














