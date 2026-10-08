using PresenceTracker.Application;

namespace PresenceTracker.Tests;

public sealed class CalendarSelectionTests
{
    private static DateOnly D(int day) => new(2026, 6, day);

    [Fact]
    public void PlainClickReplacesSelectionAndSetsAnchor()
    {
        var selected = new HashSet<DateOnly> { D(1), D(2) };
        DateOnly? anchor = D(1);

        CalendarSelection.Apply(selected, ref anchor, D(5), control: false, shift: false);

        Assert.Equal(new[] { D(5) }, selected.OrderBy(d => d));
        Assert.Equal(D(5), anchor);
    }

    [Fact]
    public void ControlClickAddsNonContiguousDays()
    {
        var selected = new HashSet<DateOnly>();
        DateOnly? anchor = null;

        CalendarSelection.Apply(selected, ref anchor, D(2), control: false, shift: false);
        CalendarSelection.Apply(selected, ref anchor, D(5), control: true, shift: false);
        CalendarSelection.Apply(selected, ref anchor, D(9), control: true, shift: false);

        Assert.Equal(new[] { D(2), D(5), D(9) }, selected.OrderBy(d => d));
        Assert.Equal(D(9), anchor);
    }

    [Fact]
    public void ControlClickTogglesDayOffWithoutClearingOthers()
    {
        var selected = new HashSet<DateOnly> { D(2), D(5), D(9) };
        DateOnly? anchor = D(9);

        CalendarSelection.Apply(selected, ref anchor, D(5), control: true, shift: false);

        Assert.Equal(new[] { D(2), D(9) }, selected.OrderBy(d => d));
        Assert.Equal(D(5), anchor);
    }

    [Fact]
    public void ShiftClickSelectsInclusiveContiguousRangeFromAnchor()
    {
        var selected = new HashSet<DateOnly>();
        DateOnly? anchor = null;

        CalendarSelection.Apply(selected, ref anchor, D(3), control: false, shift: false);
        CalendarSelection.Apply(selected, ref anchor, D(7), control: false, shift: true);

        Assert.Equal(Enumerable.Range(3, 5).Select(D), selected.OrderBy(d => d));
        Assert.Equal(D(3), anchor);
    }

    [Fact]
    public void ShiftClickWorksBackwardsFromAnchor()
    {
        var selected = new HashSet<DateOnly>();
        DateOnly? anchor = null;

        CalendarSelection.Apply(selected, ref anchor, D(10), control: false, shift: false);
        CalendarSelection.Apply(selected, ref anchor, D(7), control: false, shift: true);

        Assert.Equal(Enumerable.Range(7, 4).Select(D), selected.OrderBy(d => d));
    }

    [Fact]
    public void ControlPlusShiftPrefersRangeSelection()
    {
        var selected = new HashSet<DateOnly>();
        DateOnly? anchor = null;

        CalendarSelection.Apply(selected, ref anchor, D(1), control: false, shift: false);
        CalendarSelection.Apply(selected, ref anchor, D(4), control: true, shift: true);

        Assert.Equal(Enumerable.Range(1, 4).Select(D), selected.OrderBy(d => d));
    }

    [Fact]
    public void ShiftWithoutAnchorActsLikePlainClick()
    {
        var selected = new HashSet<DateOnly> { D(1) };
        DateOnly? anchor = null;

        CalendarSelection.Apply(selected, ref anchor, D(8), control: false, shift: true);

        Assert.Equal(new[] { D(8) }, selected.OrderBy(d => d));
        Assert.Equal(D(8), anchor);
    }

    [Fact]
    public void ControlClickOnEmptySelectionSelectsSingleDay()
    {
        var selected = new HashSet<DateOnly>();
        DateOnly? anchor = null;

        CalendarSelection.Apply(selected, ref anchor, D(4), control: true, shift: false);

        Assert.Equal(new[] { D(4) }, selected.OrderBy(d => d));
        Assert.Equal(D(4), anchor);
    }
}
