using System.Globalization;

namespace TypingStats.Core;

public enum MouseButton { Left = 1, Right = 2, Middle = 3, X1 = 4, X2 = 5 }
public sealed record MouseMetricRow(string LocalDate, string App, MouseButton Button, long Presses, long Injected);
public sealed record MouseStatisticsFilter(DateOnly From, DateOnly To, string? Application = null, MouseButton? Button = null, bool IncludeEmptyDays = false);
public sealed record DailyMouseStatistics(DateOnly Date, long Presses, int Applications);
public sealed record MouseButtonStatistics(MouseButton Button, string Label, long Presses, long Injected, double Share);
public sealed record MouseStatisticsReport(MouseStatisticsFilter Filter, IReadOnlyList<MouseMetricRow> Rows, IReadOnlyList<DailyMouseStatistics> Days)
{
    public long Presses => Rows.Sum(r => r.Presses);
    public int DataDays => Days.Count(d => d.Presses > 0);
    public int Applications => Rows.Select(r => r.App).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public int UsedButtons => Rows.Select(r => r.Button).Distinct().Count();
    public IReadOnlyList<MouseMetricRow> ForDay(DateOnly? day) => day == null ? Rows : Rows.Where(r => r.LocalDate == MouseStatistics.Iso(day.Value)).ToArray();
    public IReadOnlyList<MouseButtonStatistics> Breakdown(DateOnly? day = null)
    {
        var rows = ForDay(day); var total = Math.Max(1, rows.Sum(r => r.Presses));
        return Enum.GetValues<MouseButton>().Where(b => Filter.Button == null || Filter.Button == b).Select(b =>
        {
            var buttons = rows.Where(r => r.Button == b).ToArray(); var n = buttons.Sum(r => r.Presses);
            return new MouseButtonStatistics(b, MouseStatistics.Label(b), n, buttons.Sum(r => r.Injected), Math.Round(n * 100.0 / total, 2));
        }).ToArray();
    }
}

public static class MouseStatistics
{
    public static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string Label(MouseButton button) => button switch
    {
        MouseButton.Left => "Левая (ЛКМ)", MouseButton.Right => "Правая (ПКМ)", MouseButton.Middle => "Средняя (колесо)",
        MouseButton.X1 => "Боковая X1", MouseButton.X2 => "Боковая X2", _ => throw new ArgumentOutOfRangeException(nameof(button))
    };
    // Only down events count. Two down events in a double-click count as two presses.
    public static MouseButton? FromMessage(int message, uint mouseData) => message switch
    {
        0x201 => MouseButton.Left, 0x204 => MouseButton.Right, 0x207 => MouseButton.Middle,
        0x20b when (mouseData >> 16) == 1 => MouseButton.X1,
        0x20b when (mouseData >> 16) == 2 => MouseButton.X2, _ => null
    };
    public static bool CountPress(bool enabled, bool injected, bool countInjected) => enabled && (!injected || countInjected);
    public static MouseStatisticsReport Build(IEnumerable<MouseMetricRow> source, MouseStatisticsFilter filter)
    {
        if (filter.From > filter.To) throw new ArgumentException("Дата начала должна быть не позже даты окончания.");
        var rows = source.Where(r => r.Presses > 0 && Enum.IsDefined(r.Button)
            && DateOnly.TryParseExact(r.LocalDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d >= filter.From && d <= filter.To
            && (filter.Application == null || r.App.Equals(filter.Application, StringComparison.OrdinalIgnoreCase))
            && (filter.Button == null || r.Button == filter.Button)).ToArray();
        var days = rows.GroupBy(r => DateOnly.ParseExact(r.LocalDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)).ToDictionary(g => g.Key,
            g => new DailyMouseStatistics(g.Key, g.Sum(r => r.Presses), g.Select(r => r.App).Distinct(StringComparer.OrdinalIgnoreCase).Count()));
        if (filter.IncludeEmptyDays && filter.To.DayNumber - filter.From.DayNumber < 36525)
            for (var day = filter.From; day <= filter.To; day = day.AddDays(1))
            { days.TryAdd(day, new DailyMouseStatistics(day, 0, 0)); if (day == DateOnly.MaxValue) break; }
        return new MouseStatisticsReport(filter, rows, days.Values.OrderByDescending(d => d.Date).ToArray());
    }
}
