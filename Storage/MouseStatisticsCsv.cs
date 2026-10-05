using System.Globalization;
using System.Text;
using TypingStats.Core;

namespace TypingStats.Storage;

public enum MouseStatisticsExport { Detailed, Daily, Buttons }
public static class MouseStatisticsCsv
{
    public static string Render(MouseStatisticsReport report, MouseStatisticsExport format, DateOnly? day = null, IReadOnlyDictionary<string, ApplicationIdentity>? applications = null)
    {
        if (day != null && (day < report.Filter.From || day > report.Filter.To)) throw new ArgumentException("День вне выбранного периода.");
        var b = new StringBuilder();
        switch (format)
        {
            case MouseStatisticsExport.Detailed:
                b.AppendLine("date,application_id,application_name,executable,button_code,button_name,presses,injected");
                foreach (var g in report.ForDay(day).GroupBy(r => (r.LocalDate, r.App, r.Button)).OrderBy(g => g.Key.LocalDate).ThenBy(g => g.Key.App).ThenBy(g => g.Key.Button))
                {
                    var app = applications != null && applications.TryGetValue(g.Key.App, out var known) ? known : ApplicationIdentity.Unknown(g.Key.App);
                    Add(b, g.Key.LocalDate, g.Key.App, app.Name, app.Executable, g.Key.Button.ToString(), MouseStatistics.Label(g.Key.Button), g.Sum(r => r.Presses), g.Sum(r => r.Injected));
                }
                break;
            case MouseStatisticsExport.Daily:
                b.AppendLine("date,presses,applications");
                foreach (var d in report.Days.Where(d => day == null || d.Date == day).OrderBy(d => d.Date)) Add(b, MouseStatistics.Iso(d.Date), d.Presses, d.Applications);
                break;
            case MouseStatisticsExport.Buttons:
                b.AppendLine("button_code,button_name,presses,share_percent,injected");
                foreach (var k in report.Breakdown(day)) Add(b, k.Button.ToString(), k.Label, k.Presses, k.Share, k.Injected);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(format));
        }
        return b.ToString();
    }
    public static void Write(MouseStatisticsReport report, string path, MouseStatisticsExport format, DateOnly? day = null, IReadOnlyDictionary<string, ApplicationIdentity>? applications = null)
    {
        var content = Render(report, format, day, applications);
        var target = Path.GetFullPath(path); var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, content, new UTF8Encoding(true)); File.Move(temp, target, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void Add(StringBuilder b, params object[] values) => b.AppendLine(string.Join(",", values.Select(v => v is string s ? Cell(s) : Convert.ToString(v, CultureInfo.InvariantCulture))));
    private static string Cell(string s)
    {
        var trimmed = s.TrimStart(); if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
