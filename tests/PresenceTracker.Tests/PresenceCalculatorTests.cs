using PresenceTracker.Domain;

namespace PresenceTracker.Tests;

public sealed class PresenceCalculatorTests
{
    private readonly PresenceCalculator calculator = new();
    private static DateOnly D(int year, int month, int day) => new(year, month, day);
    private static IReadOnlyList<Holiday> NoHolidays => [];
    private static IReadOnlyList<DayClassification> NoClassifications => [];
    private static IReadOnlyList<AttendanceEvent> NoAttendance => [];
    private static IReadOnlyList<PresencePlan> NoPlans => [];

    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    [InlineData(60)]
    public void TargetDaysUseCeilingForQuickTargets(decimal target)
    {
        var result = calculator.CalculateMonth(2026, 6, D(2026, 6, 1), target, WorkingDays.Weekdays,
            NoHolidays, NoClassifications, NoAttendance, NoPlans);
        Assert.Equal((int)decimal.Ceiling(result.EligibleWorkingDays * target / 100m), result.TargetDays);
        Assert.InRange(result.MonthlyPercent, 0, 100);
    }

    [Fact]
    public void WholeNumberTargetUsesCeilingAndDoesNotRoundDown()
    {
        var holidays = new[] { new Holiday { Date = D(2026, 6, 1), Name = "Feriado 1", IsActive = true }, new Holiday { Date = D(2026, 6, 2), Name = "Feriado 2", IsActive = true } };
        var result = calculator.CalculateMonth(2026, 6, D(2026, 6, 1), 60m, WorkingDays.Weekdays,
            holidays, NoClassifications, NoAttendance, NoPlans);
        Assert.Equal(12, result.TargetDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100.1)]
    [InlineData(101)]
    [InlineData(59.9)]
    public void RejectsInvalidTargets(decimal target) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PresenceCalculator.ValidateTarget(target));

    [Fact]
    public void AcceptsOneHundredPercent()
    {
        PresenceCalculator.ValidateTarget(100m);
        var result = calculator.CalculateMonth(2026, 6, D(2026, 6, 1), 100m, WorkingDays.Weekdays,
            NoHolidays, NoClassifications, NoAttendance, NoPlans);
        Assert.Equal(result.EligibleWorkingDays, result.TargetDays);
    }

    [Fact]
    public void CurrentDayDoesNotEnterElapsedDenominatorBeforeAnOutcome()
    {
        var result = calculator.CalculateMonth(2026, 6, D(2026, 6, 2), 40m, WorkingDays.Weekdays,
            NoHolidays, NoClassifications, NoAttendance, NoPlans);
        Assert.Equal(1, result.ElapsedWorkingDays);
        Assert.Equal(0, result.RealizedDays);
    }

    [Fact]
    public void AttendanceMakesCurrentDayCountOnceEvenWithMultipleAutomaticEvents()
    {
        var events = new[]
        {
            new AttendanceEvent { Date = D(2026, 6, 2), Source = AttendanceSource.Automatic, Status = AttendanceStatus.Active },
            new AttendanceEvent { Date = D(2026, 6, 2), Source = AttendanceSource.Automatic, Status = AttendanceStatus.Active }
        };
        var result = calculator.CalculateMonth(2026, 6, D(2026, 6, 2), 40m, WorkingDays.Weekdays,
            NoHolidays, NoClassifications, events, NoPlans);
        Assert.Equal(1, result.RealizedDays);
        Assert.Equal(2, result.ElapsedWorkingDays);
    }

    [Fact]
    public void ExcludedAutomaticPresenceRemainsAnEventButDoesNotCount()
    {
        var attendance = new[]
        {
            new AttendanceEvent { Id = 1, Date = D(2026, 6, 1), Source = AttendanceSource.Automatic, Status = AttendanceStatus.Excluded }
        };
        var outcome = calculator.EvaluateDay(D(2026, 6, 1), WorkingDays.Weekdays, null, null, attendance, false);
        Assert.False(outcome.CountsAsPresence);
        Assert.True(outcome.HasExcludedAutomaticAttendance);
    }

    [Fact]
    public void ManualAndAutomaticAttendanceAreDeduplicatedAndRespectAbsencePrecedence()
    {
        var date = D(2026, 6, 1);
        var events = new[]
        {
            new AttendanceEvent { Date = date, Source = AttendanceSource.Automatic, Status = AttendanceStatus.Active },
            new AttendanceEvent { Date = date, Source = AttendanceSource.Manual, Status = AttendanceStatus.Active }
        };
        var holiday = new Holiday { Date = date, Name = "Corpus Christi", IsActive = true };
        Assert.False(calculator.EvaluateDay(date, WorkingDays.Weekdays, holiday, null, events, false).CountsAsPresence);
        var vacation = new DayClassification { Date = date, Type = AbsenceType.Vacation };
        Assert.False(calculator.EvaluateDay(date, WorkingDays.Weekdays, null, vacation, events, false).CountsAsPresence);
        Assert.True(calculator.EvaluateDay(date, WorkingDays.Weekdays, null, null, events, false).CountsAsPresence);
    }

    [Theory]
    [InlineData(AbsenceType.Vacation)]
    [InlineData(AbsenceType.DayOff)]
    [InlineData(AbsenceType.Holiday)]
    [InlineData(AbsenceType.NonWorkingDay)]
    public void EveryManualAbsenceExcludesTheDate(AbsenceType type)
    {
        var date = D(2026, 6, 1);
        var result = calculator.CalculateMonth(2026, 6, date, 40m, WorkingDays.Weekdays,
            NoHolidays, new[] { new DayClassification { Date = date, Type = type } },
            new[] { new AttendanceEvent { Date = date, Source = AttendanceSource.Manual, Status = AttendanceStatus.Active } }, NoPlans);
        Assert.Equal(21, result.EligibleWorkingDays);
        Assert.Equal(0, result.RealizedDays);
    }

    [Fact]
    public void PlansProjectFuturePresenceAndDoNotDuplicateActualPresence()
    {
        var actualDate = D(2026, 6, 1);
        var planDate = D(2026, 6, 2);
        var actual = new[] { new AttendanceEvent { Date = actualDate, Source = AttendanceSource.Manual, Status = AttendanceStatus.Active } };
        var plans = new[] { new PresencePlan { Date = actualDate }, new PresencePlan { Date = planDate } };
        var result = calculator.CalculateMonth(2026, 6, D(2026, 6, 1), 40m, WorkingDays.Weekdays,
            NoHolidays, NoClassifications, actual, plans);
        Assert.Equal(1, result.RealizedDays);
        Assert.Equal(1, result.PlannedDays);
        Assert.True(result.ProjectedPercent > result.MonthlyPercent);
    }

    [Fact]
    public void NonWorkdaysAndExclusionsAffectMonthAndYearBoundaries()
    {
        var date = D(2026, 12, 31);
        Assert.False(PresenceCalculator.IsConfiguredWorkday(D(2027, 1, 2), WorkingDays.Weekdays));
        var holiday = new Holiday { Date = date, Name = "Ano Novo", IsActive = true };
        var result = calculator.CalculateMonth(2026, 12, date, 40m, WorkingDays.Weekdays,
            new[] { holiday }, NoClassifications, NoAttendance, NoPlans);
        Assert.Equal(22, result.EligibleWorkingDays);
    }

    [Fact]
    public void HistoricalAverageIsSimpleAndEmptyHistoryIsZero()
    {
        Assert.Equal(40m, PresenceCalculator.SimpleMonthlyAverage(new[] { 40m, 50m, 30m }));
        Assert.Equal(0m, PresenceCalculator.SimpleMonthlyAverage([]));
    }

    [Fact]
    public async Task LocalCalendarContainsNationalStateAndSaoPauloMunicipalHolidays()
    {
        var provider = new PresenceTracker.Infrastructure.LocalHolidayProvider();
        foreach (var year in new[] { 2026, 2027 })
        {
            var holidays = await provider.GetHolidaysAsync(year);
            Assert.Contains(holidays, h => h.Date == D(year, 1, 1) && h.Scope == HolidayScope.National);
            Assert.Contains(holidays, h => h.Date == D(year, 7, 9) && h.Scope == HolidayScope.State);
            Assert.Contains(holidays, h => h.Date == D(year, 1, 25) && h.Scope == HolidayScope.Municipal);
        }
    }
}








