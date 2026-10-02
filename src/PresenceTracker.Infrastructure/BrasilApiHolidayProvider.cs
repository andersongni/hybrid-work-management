using System.Net.Http.Json;
using System.Text.Json;
using PresenceTracker.Domain;
using PresenceTracker.Application;

namespace PresenceTracker.Infrastructure;

public sealed class BrasilApiHolidayProvider(HttpClient client) : IHolidayProvider
{
    private sealed record HolidayDto(string Date, string Name, string Type);

    public async Task<IReadOnlyList<Holiday>> GetHolidaysAsync(int year, CancellationToken cancellationToken = default)
    {
        if (year is < 1900 or > 2199)
            throw new ArgumentOutOfRangeException(nameof(year));
        using var response = await client.GetAsync($"https://brasilapi.com.br/api/feriados/v1/{year}?uf=SP", cancellationToken);
        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<HolidayDto>>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken)
            ?? throw new InvalidDataException("A BrasilAPI retornou uma lista de feriados vazia ou inválida.");
        return items.Where(item => !item.Name.Contains("carnaval", StringComparison.OrdinalIgnoreCase)
                && !item.Name.Contains("paixão de cristo", StringComparison.OrdinalIgnoreCase)
                && !item.Name.Contains("corpus christi", StringComparison.OrdinalIgnoreCase))
            .Select(item => new Holiday
        {
            Date = DateOnly.Parse(item.Date),
            Name = item.Name.Trim(),
            Scope = item.Type.Equals("regional", StringComparison.OrdinalIgnoreCase) ? HolidayScope.State : HolidayScope.National,
            Source = HolidaySource.Synchronized,
            IsActive = true
        }).ToArray();
    }
}








