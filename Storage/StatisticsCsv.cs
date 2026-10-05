using System.Globalization;
using System.Text;
using TypingStats.Core;

namespace TypingStats.Storage;

public enum StatisticsExport { Detailed, Daily, Keys }
public static class StatisticsCsv
{
    public static void Write(KeyStatisticsReport report, string path, StatisticsExport format, DateOnly? day = null,IReadOnlyDictionary<string,ApplicationIdentity>? applications=null)
    {
        if (day != null && (day < report.Filter.From || day > report.Filter.To)) throw new ArgumentException("День вне выбранного периода.");
        var rows = report.Rows.Where(r => day == null || r.Key.LocalDate == Iso(day.Value)).ToArray();
        // Build completely before replacing an existing file; a failed export leaves it intact.
        var content = Render(report, rows, format, day,applications);
        var target = Path.GetFullPath(path); var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, content, new UTF8Encoding(true)); File.Move(temp, target, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static string Render(KeyStatisticsReport report, IReadOnlyList<KeyMetricRow> rows, StatisticsExport format, DateOnly? day = null,IReadOnlyDictionary<string,ApplicationIdentity>? applications=null)
    {
        var b = new StringBuilder();
        switch (format)
        {
            case StatisticsExport.Detailed:
                b.AppendLine("date,application_id,application_name,executable,input_profile,key_code,key_name,presses,repeats,injected");
                foreach (var g in rows.GroupBy(r => (r.Key.LocalDate, r.Key.App, r.Key.Profile, r.Code)).OrderBy(g => g.Key.LocalDate).ThenBy(g => g.Key.App).ThenBy(g => g.Key.Code))
                {var app=applications!=null&&applications.TryGetValue(g.Key.App,out var known)?known:ApplicationIdentity.Unknown(g.Key.App);
                    Add(b, g.Key.LocalDate, g.Key.App, app.Name,app.Executable,g.Key.Profile, g.Key.Code, g.First().Label, g.Sum(r => r.Presses), g.Sum(r => r.Repeats), g.Sum(r => r.Injected));}
                break;
            case StatisticsExport.Daily:
                b.AppendLine("date,presses,distinct_keys,applications,repeats,top_key");
                foreach (var d in report.Days.Where(d => day == null || d.Date == day.Value).OrderBy(d => d.Date)) Add(b, Iso(d.Date), d.Presses, d.DistinctKeys, d.Applications, d.Repeats, d.TopKey);
                break;
            case StatisticsExport.Keys:
                b.AppendLine("key_code,key_name,presses,share_percent,repeats,injected,applications");
                foreach (var k in KeyStatistics.Breakdown(rows)) Add(b, k.Code, k.Label, k.Presses, k.Share, k.Repeats, k.Injected, k.Applications);
                break;
        }
        return b.ToString();
    }
    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static void Add(StringBuilder b, params object[] values) => b.AppendLine(string.Join(",", values.Select(v =>
        v is string s ? Cell(s) : Convert.ToString(v, CultureInfo.InvariantCulture))));
    private static string Cell(string s)
    {
        var trimmed = s.TrimStart(); if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
