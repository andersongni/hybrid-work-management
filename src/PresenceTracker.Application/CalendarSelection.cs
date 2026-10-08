namespace PresenceTracker.Application;

/// <summary>
/// Pure multi-day calendar selection rules (plain click, Ctrl toggle, Shift range).
/// Modifiers must be sampled on mouse down — by click/mouse-up Ctrl is often already released.
/// </summary>
public static class CalendarSelection
{
    public static void Apply(ISet<DateOnly> selectedDates, ref DateOnly? selectionAnchor, DateOnly date,
        bool control, bool shift)
    {
        ArgumentNullException.ThrowIfNull(selectedDates);

        if (shift && selectionAnchor is { } anchor)
        {
            selectedDates.Clear();
            var first = anchor < date ? anchor : date;
            var last = anchor > date ? anchor : date;
            for (var current = first; current <= last; current = current.AddDays(1))
                selectedDates.Add(current);
            return;
        }

        if (control)
        {
            if (!selectedDates.Add(date))
                selectedDates.Remove(date);
            selectionAnchor = date;
            return;
        }

        selectedDates.Clear();
        selectedDates.Add(date);
        selectionAnchor = date;
    }
}
