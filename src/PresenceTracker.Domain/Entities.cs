namespace PresenceTracker.Domain;

[Flags]
public enum WorkingDays { None = 0, Monday = 1, Tuesday = 2, Wednesday = 4, Thursday = 8, Friday = 16, Saturday = 32, Sunday = 64, Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday }
public enum HolidayScope { National, State, Municipal, Custom }
public enum HolidaySource { System, Synchronized, Manual }
public enum AbsenceType { Vacation, DayOff, Holiday, NonWorkingDay }
public enum AttendanceSource { Automatic, Manual }
public enum AttendanceStatus { Active, Excluded }
public enum NetworkEventType { Connected, Disconnected }
public enum ThemeMode { System, Light, Dark }

public sealed class TrackerSettings
{
    public int Id { get; set; } = 1;
    public decimal TargetPercent { get; set; } = 40m;
    public WorkingDays WorkingDays { get; set; } = WorkingDays.Weekdays;
    public bool StartWithWindows { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public int BackupRetentionCount { get; set; } = 30;
    public string MinimumLogLevel { get; set; } = "Information";
}

public sealed class PresenceNetwork
{
    public int Id { get; set; }
    public string Ssid { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool CountsAsPresence { get; set; } = true;
}

public sealed class Holiday
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
    public HolidayScope Scope { get; set; }
    public HolidaySource Source { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DayClassification
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public AbsenceType Type { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AttendanceEvent
{
    public long Id { get; set; }
    public DateOnly Date { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public AttendanceSource Source { get; set; }
    public AttendanceStatus Status { get; set; } = AttendanceStatus.Active;
    public long? NetworkEventId { get; set; }
}

public sealed class NetworkEvent
{
    public long Id { get; set; }
    public DateOnly Date { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Ssid { get; set; }
    public string InterfaceId { get; set; } = "";
    public string InterfaceName { get; set; } = "";
    public NetworkEventType Type { get; set; }
}

public sealed class PresencePlan
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class MonthlySnapshot
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal TargetPercent { get; set; }
    public WorkingDays WorkingDays { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed record DayOutcome(DateOnly Date, bool IsWorkingDay, bool IsHoliday, string? HolidayName,
    AbsenceType? Absence, bool HasAutomaticAttendance, bool HasManualAttendance,
    bool HasExcludedAutomaticAttendance, bool IsPlanned, bool CountsAsPresence, string Reason);

public sealed record MonthMetrics(int Year, int Month, decimal TargetPercent, int EligibleWorkingDays,
    int ElapsedWorkingDays, int RealizedDays, int RealizedThroughToday, int TargetDays, int DaysNeeded, int DaysRemaining,
    int PlannedDays, decimal CurrentPacePercent, decimal MonthlyPercent, decimal ProjectedPercent,
    bool TargetReached, bool TargetReachableByPlan);






