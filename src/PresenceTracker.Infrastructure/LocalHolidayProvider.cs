using PresenceTracker.Domain;
using PresenceTracker.Application;

namespace PresenceTracker.Infrastructure;

public sealed class LocalHolidayProvider : IHolidayProvider
{
    public Task<IReadOnlyList<Holiday>> GetHolidaysAsync(int year, CancellationToken cancellationToken = default)
    {
        if (year is < 1900 or > 2199)
            throw new ArgumentOutOfRangeException(nameof(year));
        var result = new List<Holiday>();
        void Add(DateOnly date, string name, HolidayScope scope = HolidayScope.National) =>
            result.Add(new Holiday { Date = date, Name = name, Scope = scope, Source = HolidaySource.System, IsActive = true });

        Add(new DateOnly(year, 1, 1), "Confraternização Universal");
        Add(new DateOnly(year, 4, 21), "Tiradentes");
        Add(new DateOnly(year, 5, 1), "Dia do Trabalho");
        Add(new DateOnly(year, 9, 7), "Independência do Brasil");
        Add(new DateOnly(year, 10, 12), "Nossa Senhora Aparecida");
        Add(new DateOnly(year, 11, 2), "Finados");
        Add(new DateOnly(year, 11, 15), "Proclamação da República");
        Add(new DateOnly(year, 11, 20), "Dia da Consciência Negra");
        Add(new DateOnly(year, 12, 25), "Natal");
        var easter = EasterSunday(year);
        Add(easter.AddDays(-2), "Sexta-feira Santa", HolidayScope.Municipal);
        Add(easter.AddDays(60), "Corpus Christi", HolidayScope.Municipal);
        Add(new DateOnly(year, 1, 25), "Aniversário da cidade de São Paulo", HolidayScope.Municipal);
        Add(new DateOnly(year, 7, 9), "Revolução Constitucionalista de 1932", HolidayScope.State);
        return Task.FromResult<IReadOnlyList<Holiday>>(result);
    }

    private static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }
}








