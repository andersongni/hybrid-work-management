using PresenceTracker.Application;
using PresenceTracker.Domain;

namespace PresenceTracker.Tests;

public sealed class TrackerServiceTests
{
    private static TrackerService CreateService(FakeTrackerRepository repository) =>
        new(repository, new PresenceCalculator());

    private static TrackerSettings ValidSettings(
        int retention = 30, int backupHours = 24, int wifiMinutes = 10, decimal target = 40m) =>
        new()
        {
            TargetPercent = target,
            BackupRetentionCount = retention,
            BackupIntervalHours = backupHours,
            WifiCheckIntervalMinutes = wifiMinutes
        };

    private static PresenceNetwork Network(string ssid) => new() { Ssid = ssid, IsActive = true };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateSettingsRejectsBlankSsids(string ssid)
    {
        var service = CreateService(new FakeTrackerRepository());
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateSettingsAsync(ValidSettings(), [Network(ssid)]));
        Assert.Equal("networks", ex.ParamName);
    }

    [Fact]
    public async Task UpdateSettingsRejectsSsidLongerThan32Utf8Bytes()
    {
        var service = CreateService(new FakeTrackerRepository());
        var longSsid = new string('a', 33);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateSettingsAsync(ValidSettings(), [Network(longSsid)]));
        Assert.Equal("networks", ex.ParamName);
    }

    [Fact]
    public async Task UpdateSettingsRejectsDuplicateSsidsIgnoringCase()
    {
        var service = CreateService(new FakeTrackerRepository());
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateSettingsAsync(ValidSettings(), [Network("CORP"), Network("corp")]));
        Assert.Equal("networks", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public async Task UpdateSettingsRejectsBackupRetentionOutsideRange(int retention)
    {
        var service = CreateService(new FakeTrackerRepository());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.UpdateSettingsAsync(ValidSettings(retention: retention), [Network("CORP")]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(721)]
    public async Task UpdateSettingsRejectsBackupIntervalOutsideRange(int hours)
    {
        var service = CreateService(new FakeTrackerRepository());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.UpdateSettingsAsync(ValidSettings(backupHours: hours), [Network("CORP")]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public async Task UpdateSettingsRejectsWifiIntervalOutsideRange(int minutes)
    {
        var service = CreateService(new FakeTrackerRepository());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.UpdateSettingsAsync(ValidSettings(wifiMinutes: minutes), [Network("CORP")]));
    }

    [Fact]
    public async Task UpdateSettingsRejectsInvalidTarget()
    {
        var service = CreateService(new FakeTrackerRepository());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.UpdateSettingsAsync(ValidSettings(target: 0m), [Network("CORP")]));
    }

    [Fact]
    public async Task UpdateSettingsForwardsValidPayloadToRepository()
    {
        var repository = new FakeTrackerRepository();
        var service = CreateService(repository);
        var settings = ValidSettings(retention: 7, backupHours: 12, wifiMinutes: 5, target: 60m);
        var networks = new[] { Network("CORP"), Network("HOME") };

        var result = await service.UpdateSettingsAsync(settings, networks);

        Assert.Same(settings, result);
        Assert.Same(settings, repository.LastUpdatedSettings);
        Assert.Equal(2, repository.LastUpdatedNetworks!.Count);
    }

    [Fact]
    public async Task GetMonthClearsPastPlansAndIgnoresThemInDayOutcomes()
    {
        var today = new DateOnly(2026, 6, 10);
        var repository = new FakeTrackerRepository
        {
            Plans =
            {
                new PresencePlan { Date = today.AddDays(-1) },
                new PresencePlan { Date = today },
                new PresencePlan { Date = today.AddDays(1) }
            }
        };
        var service = CreateService(repository);

        var month = await service.GetMonthAsync(2026, 6, today);

        Assert.Equal(1, repository.ClearPlansBeforeCalls);
        Assert.Equal(today, repository.LastClearPlansBefore);
        Assert.DoesNotContain(repository.Plans, plan => plan.Date < today);
        Assert.False(month.Days.Single(d => d.Outcome.Date == today.AddDays(-1)).Outcome.IsPlanned);
        Assert.True(month.Days.Single(d => d.Outcome.Date == today).Outcome.IsPlanned);
        Assert.True(month.Days.Single(d => d.Outcome.Date == today.AddDays(1)).Outcome.IsPlanned);
    }

    [Fact]
    public async Task GetMonthPrefersManualHolidayOverSynchronizedOnSameDate()
    {
        var date = new DateOnly(2026, 6, 1);
        var repository = new FakeTrackerRepository
        {
            Holidays =
            {
                new Holiday
                {
                    Date = date, Name = "Remoto", Scope = HolidayScope.National,
                    Source = HolidaySource.Synchronized, IsActive = true
                },
                new Holiday
                {
                    Date = date, Name = "Manual", Scope = HolidayScope.Custom,
                    Source = HolidaySource.Manual, IsActive = true
                }
            }
        };
        var service = CreateService(repository);

        var month = await service.GetMonthAsync(2026, 6, date);
        var day = month.Days.Single(d => d.Outcome.Date == date);

        Assert.True(day.Outcome.IsHoliday);
        Assert.Equal("Manual", day.Outcome.HolidayName);
    }

    [Fact]
    public async Task ApplyBatchDeduplicatesDatesBeforeCallingRepository()
    {
        var repository = new FakeTrackerRepository();
        var service = CreateService(repository);
        var date = new DateOnly(2026, 6, 1);

        var result = await service.ApplyBatchAsync([date, date, date.AddDays(1)], BatchAction.Vacation);

        Assert.Equal(2, result.AppliedCount);
        Assert.Empty(result.SkippedPastPlanDates);
        Assert.Equal(BatchAction.Vacation, repository.LastBatchAction);
        Assert.Equal(2, repository.LastBatchDates!.Count);
        Assert.Contains(date, repository.LastBatchDates);
        Assert.Contains(date.AddDays(1), repository.LastBatchDates);
    }

    [Fact]
    public async Task PlanPresenceSkipsPastDatesAndDoesNotCallRepositoryWhenAllArePast()
    {
        var repository = new FakeTrackerRepository();
        var service = CreateService(repository);
        var today = new DateOnly(2026, 6, 10);

        var result = await service.ApplyBatchAsync(
            [today.AddDays(-2), today.AddDays(-1)], BatchAction.PlanPresence, today: today);

        Assert.Equal(0, result.AppliedCount);
        Assert.Equal(2, result.SkippedPastPlanDates.Count);
        Assert.Null(repository.LastBatchDates);
        Assert.Contains("anteriores a hoje", result.PastPlanSkippedMessage);
    }

    [Fact]
    public async Task PlanPresenceAppliesTodayAndFutureWhileReportingSkippedPastDates()
    {
        var repository = new FakeTrackerRepository();
        var service = CreateService(repository);
        var today = new DateOnly(2026, 6, 10);
        var past = today.AddDays(-1);
        var future = today.AddDays(2);

        var result = await service.ApplyBatchAsync(
            [past, today, future], BatchAction.PlanPresence, today: today);

        Assert.Equal(2, result.AppliedCount);
        Assert.Equal(new[] { past }, result.SkippedPastPlanDates);
        Assert.Equal(new[] { today, future }, repository.LastBatchDates);
        Assert.Contains("ignorado", result.PastPlanSkippedMessage);
    }

    [Theory]
    [InlineData(1, 0, "Não é possível planejar um dia anterior a hoje")]
    [InlineData(3, 0, "Não é possível planejar 3 dias anteriores a hoje")]
    [InlineData(1, 2, "1 dia no passado foi ignorado")]
    [InlineData(2, 1, "2 dias no passado foram ignorados")]
    public void PastPlanSkippedMessageExplainsWhyPlanningWasIgnored(int skipped, int applied, string expectedFragment)
    {
        var message = BatchApplyResult.FormatPastPlanSkippedMessage(skipped, applied);
        Assert.Contains(expectedFragment, message);
        Assert.Contains("hoje e datas futuras", message);
    }

    [Fact]
    public async Task SynchronizeHolidaysMergesProviderResults()
    {
        var repository = new FakeTrackerRepository();
        var service = CreateService(repository);
        var remote = new[]
        {
            new Holiday
            {
                Date = new DateOnly(2026, 9, 7), Name = "Independência",
                Scope = HolidayScope.National, Source = HolidaySource.Synchronized
            }
        };

        await service.SynchronizeHolidaysAsync(2026, new FakeHolidayProvider(remote));

        Assert.NotNull(repository.LastMergedHolidays);
        Assert.Equal(remote, repository.LastMergedHolidays);
    }
}
