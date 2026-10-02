using PresenceTracker.Domain;
using PresenceTracker.Application;

namespace PresenceTracker.Infrastructure;

public sealed class CombinedHolidayProvider(LocalHolidayProvider local, IHolidayProvider remote) : IHolidayProvider
{
    public async Task<IReadOnlyList<Holiday>> GetHolidaysAsync(int year, CancellationToken cancellationToken = default)
    {
        var localItems = await local.GetHolidaysAsync(year, cancellationToken);
        var remoteItems = await remote.GetHolidaysAsync(year, cancellationToken);
        return localItems.Concat(remoteItems)
            .GroupBy(x => (x.Date, x.Scope))
            .Select(group => group.OrderByDescending(x => x.Source == HolidaySource.Synchronized).First())
            .OrderBy(x => x.Date)
            .ToArray();
    }
}



