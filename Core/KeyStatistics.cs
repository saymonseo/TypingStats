using System.Globalization;

namespace TypingStats.Core;

public enum KeyGroup { All, Letters, Digits, Modifiers, Navigation, Functions, Numpad, Symbols, Other }
public sealed record StatisticsFilter(DateOnly From, DateOnly To, string? Application = null, KeyGroup Group = KeyGroup.All, string Search = "", bool IncludeEmptyDays = false);
public sealed record DailyKeyStatistics(DateOnly Date, long Presses, int DistinctKeys, int Applications, long Repeats, string TopKey);
public sealed record KeyBreakdown(string Code, string Label, long Presses, long Repeats, long Injected, double Share, int Applications);
public sealed record KeyStatisticsReport(StatisticsFilter Filter, IReadOnlyList<KeyMetricRow> Rows, IReadOnlyList<DailyKeyStatistics> Days, long Presses, int DistinctKeys, int DataDays, int Applications, string TopKey)
{
    public IReadOnlyList<KeyBreakdown> Breakdown(DateOnly? day = null) => KeyStatistics.Breakdown(Rows.Where(r => day == null || r.Key.LocalDate == day.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
}

public static class KeyStatistics
{
    private static readonly HashSet<int> Modifiers = [0x1d, 0x2a, 0x36, 0x38, 0xe01d, 0xe038, 0xe05b, 0xe05c];
    private static readonly HashSet<int> Navigation = [0x01, 0x0e, 0x0f, 0x1c, 0xe047, 0xe048, 0xe049, 0xe04b, 0xe04d, 0xe04f, 0xe050, 0xe051, 0xe052, 0xe053];
    private static readonly HashSet<int> Numpad = [0x37, 0x47, 0x48, 0x49, 0x4a, 0x4b, 0x4c, 0x4d, 0x4e, 0x4f, 0x50, 0x51, 0x52, 0x53, 0xe01c, 0xe035, 0xe045];
    private static readonly HashSet<int> Symbols = [0x0c, 0x0d, 0x1a, 0x1b, 0x27, 0x28, 0x29, 0x2b, 0x33, 0x34, 0x35, 0x39];
    public static KeyGroup GroupOf(string code)
    {
        if (!code.StartsWith("sc:", StringComparison.OrdinalIgnoreCase) || !int.TryParse(code.AsSpan(3), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var scan)) return KeyGroup.Other;
        if (scan is >= 0x10 and <= 0x19 or >= 0x1e and <= 0x26 or >= 0x2c and <= 0x32) return KeyGroup.Letters;
        if (scan is >= 0x02 and <= 0x0b) return KeyGroup.Digits;
        if (Modifiers.Contains(scan)) return KeyGroup.Modifiers;
        if (Numpad.Contains(scan)) return KeyGroup.Numpad;
        if (Navigation.Contains(scan)) return KeyGroup.Navigation;
        if (scan is >= 0x3b and <= 0x44 or 0x57 or 0x58) return KeyGroup.Functions;
        return Symbols.Contains(scan) ? KeyGroup.Symbols : KeyGroup.Other;
    }
    public static bool Matches(KeyMetricRow row, string search)
    {
        var q = search.Trim(); if (q.Length == 0) return true;
        q = q.ToLowerInvariant() switch { "пробел" => "Space", "шифт" => "Shift", "контрол" or "контролл" => "Ctrl", "альт" => "Alt", "ввод" => "Enter", "эскейп" => "Esc", _ => q };
        if (q.Length == 1) return row.Label.Split([' ', '/'], StringSplitOptions.RemoveEmptyEntries).Any(p => p.Equals(q, StringComparison.OrdinalIgnoreCase));
        return row.Label.Contains(q, StringComparison.OrdinalIgnoreCase) || row.Code.Contains(q, StringComparison.OrdinalIgnoreCase);
    }
    public static KeyStatisticsReport Build(IEnumerable<KeyMetricRow> source, StatisticsFilter filter)
    {
        if (filter.From > filter.To) throw new ArgumentException("Дата начала должна быть не позже даты окончания.");
        var rows = source.Where(r => r.Presses > 0 && DateOnly.TryParseExact(r.Key.LocalDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            && d >= filter.From && d <= filter.To && (filter.Application == null || r.Key.App.Equals(filter.Application, StringComparison.OrdinalIgnoreCase))
            && (filter.Group == KeyGroup.All || GroupOf(r.Code) == filter.Group) && Matches(r, filter.Search)).ToArray();
        var days = rows.GroupBy(r => DateOnly.ParseExact(r.Key.LocalDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)).ToDictionary(g => g.Key,
            g => new DailyKeyStatistics(g.Key, g.Sum(r => r.Presses), g.Select(r => r.Code).Distinct().Count(), g.Select(r => r.Key.App).Distinct(StringComparer.OrdinalIgnoreCase).Count(), g.Sum(r => r.Repeats), Breakdown(g).First().Label));
        var dataDays = days.Count;
        if (filter.IncludeEmptyDays && filter.To.DayNumber - filter.From.DayNumber < 36525)
            for (var day = filter.From; day <= filter.To; day = day.AddDays(1))
            { days.TryAdd(day, new DailyKeyStatistics(day, 0, 0, 0, 0, "Нет записей")); if (day == DateOnly.MaxValue) break; }
        var totals = Breakdown(rows);
        return new KeyStatisticsReport(filter, rows, days.Values.OrderByDescending(d => d.Date).ToArray(), rows.Sum(r => r.Presses), totals.Count, dataDays,
            rows.Select(r => r.Key.App).Distinct(StringComparer.OrdinalIgnoreCase).Count(), totals.FirstOrDefault()?.Label ?? "—");
    }
    public static IReadOnlyList<KeyBreakdown> Breakdown(IEnumerable<KeyMetricRow> source)
    {
        var rows = source.ToArray(); var total = Math.Max(1, rows.Sum(r => r.Presses));
        return rows.GroupBy(r => r.Code).Select(g => new KeyBreakdown(g.Key, g.First().Label, g.Sum(r => r.Presses), g.Sum(r => r.Repeats), g.Sum(r => r.Injected),
            Math.Round(g.Sum(r => r.Presses) * 100.0 / total, 2), g.Select(r => r.Key.App).Distinct(StringComparer.OrdinalIgnoreCase).Count()))
            .OrderByDescending(k => k.Presses).ThenBy(k => k.Label, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
