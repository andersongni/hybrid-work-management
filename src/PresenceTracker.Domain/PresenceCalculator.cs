namespace PresenceTracker.Domain;

public sealed class PresenceCalculator
{
    public static void ValidateTarget(decimal percent)
    {
        if (percent is <= 0m or > 100m || decimal.Truncate(percent) != percent)
            throw new ArgumentOutOfRangeException(nameof(percent), "A meta deve ser um número inteiro entre 1% e 100%.");
    }

    public MonthMetrics CalculateMonth(int year, int month, DateOnly today, decimal targetPercent,
        WorkingDays workingDays, IEnumerable<Holiday> holidays,
        IEnumerable<DayClassification> classifications, IEnumerable<AttendanceEvent> attendanceEvents,
        IEnumerable<PresencePlan> plans)
    {
        ValidateTarget(targetPercent);
        var first = new DateOnly(year, month, 1);
        var count = DateTime.DaysInMonth(year, month);
        var holidayByDate = holidays.Where(h => h.IsActive).GroupBy(h => h.Date)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(h => h.Source == HolidaySource.Manual).First());
        var classificationByDate = classifications.GroupBy(c => c.Date)
            .ToDictionary(group => group.Key, group => group.Last());
        var attendanceByDate = attendanceEvents.GroupBy(e => e.Date)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var plannedDates = plans.Select(p => p.Date).ToHashSet();

        var eligible = 0;
        var elapsed = 0;
        var realized = 0;
        var realizedThroughToday = 0;
        var planned = 0;
        for (var i = 0; i < count; i++)
        {
            var date = first.AddDays(i);
            if (!IsConfiguredWorkday(date, workingDays) || holidayByDate.ContainsKey(date) || classificationByDate.ContainsKey(date))
                continue;
            eligible++;
            attendanceByDate.TryGetValue(date, out var events);
            var hasPresence = events?.Any(e => e.Status == AttendanceStatus.Active) == true;
            if (date < today || date == today && hasPresence)
                elapsed++;
            if (hasPresence)
            {
                realized++;
                if (date <= today) realizedThroughToday++;
            }
            if (plannedDates.Contains(date) && date >= today && !hasPresence)
                planned++;
        }

        var remaining = 0;
        for (var i = 0; i < count; i++)
        {
            var date = first.AddDays(i);
            if (date < today || !IsConfiguredWorkday(date, workingDays) || holidayByDate.ContainsKey(date) || classificationByDate.ContainsKey(date))
                continue;
            attendanceByDate.TryGetValue(date, out var events);
            if (events?.Any(e => e.Status == AttendanceStatus.Active) != true)
                remaining++;
        }

        var targetDays = (int)decimal.Ceiling(eligible * targetPercent / 100m);
        var currentPace = elapsed == 0 ? 0m : Math.Min(100m, realizedThroughToday * 100m / elapsed);
        var monthly = eligible == 0 ? 0m : Math.Min(100m, realized * 100m / eligible);
        var projectedDays = Math.Min(eligible, realized + planned);
        var projected = eligible == 0 ? 0m : Math.Min(100m, projectedDays * 100m / eligible);
        var needed = Math.Max(0, targetDays - realized);
        return new MonthMetrics(year, month, targetPercent, eligible, elapsed, realized, realizedThroughToday, targetDays,
            needed, remaining, planned, currentPace, monthly, projected, realized >= targetDays,
            projectedDays >= targetDays);
    }

    public DayOutcome EvaluateDay(DateOnly date, WorkingDays workingDays, Holiday? holiday,
        DayClassification? classification, IEnumerable<AttendanceEvent> events, bool isPlanned)
    {
        var list = events.ToArray();
        var manual = list.Any(e => e.Source == AttendanceSource.Manual && e.Status == AttendanceStatus.Active);
        var automatic = list.Any(e => e.Source == AttendanceSource.Automatic && e.Status == AttendanceStatus.Active);
        var excluded = list.Any(e => e.Source == AttendanceSource.Automatic && e.Status == AttendanceStatus.Excluded);
        var workday = IsConfiguredWorkday(date, workingDays);
        var counts = workday && holiday is null && classification is null && (manual || automatic);
        var reason = holiday is not null ? "Feriado" :
            classification is not null ? AbsenceLabel(classification.Type) :
            !workday ? "Dia n\u00e3o configurado como \u00fatil" :
            counts ? "Presen\u00e7a v\u00e1lida" :
            isPlanned ? "Planejado, ainda sem resultado" : "Sem presen\u00e7a registrada";
        return new DayOutcome(date, workday, holiday is not null, holiday?.Name, classification?.Type,
            automatic, manual, excluded, isPlanned, counts, reason);
    }

    public static bool IsConfiguredWorkday(DateOnly date, WorkingDays workingDays) =>
        (workingDays & (date.DayOfWeek switch
        {
            DayOfWeek.Monday => WorkingDays.Monday,
            DayOfWeek.Tuesday => WorkingDays.Tuesday,
            DayOfWeek.Wednesday => WorkingDays.Wednesday,
            DayOfWeek.Thursday => WorkingDays.Thursday,
            DayOfWeek.Friday => WorkingDays.Friday,
            DayOfWeek.Saturday => WorkingDays.Saturday,
            _ => WorkingDays.Sunday
        })) != 0;

    public static decimal SimpleMonthlyAverage(IEnumerable<decimal> percentages)
    {
        var values = percentages.ToArray();
        return values.Length == 0 ? 0m : values.Average();
    }

    public static string AbsenceLabel(AbsenceType type) => type switch
    {
        AbsenceType.Vacation => "F\u00e9rias",
        AbsenceType.DayOff => "Day off",
        AbsenceType.Holiday => "Feriado / n\u00e3o trabalhado",
        _ => "Dia n\u00e3o trabalhado"
    };
}





