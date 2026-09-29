using System.Drawing.Drawing2D;
using TypingStats.Core;

namespace TypingStats.App.UI;

internal sealed class HistoryChart : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<(string Label, long Gross, long Net)> Values { get; set; } = [];
    public HistoryChart() { DoubleBuffered = true; BackColor = Color.White; Font = new Font("Segoe UI", 9); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var ink = new SolidBrush(Color.FromArgb(95, 110, 125));
        if (Values.Count == 0) { g.DrawString("Пока нет данных за этот период", Font, ink, 20, 20); return; }
        var width = Math.Max(1, Width - 90); var height = Math.Max(1, Height - 95);
        var max = Math.Max(1, Values.Max(x => x.Gross));
        using var grid = new Pen(Color.FromArgb(231, 236, 241));
        for (var i = 0; i <= 4; i++)
        {
            var y = 20 + height * i / 4f; g.DrawLine(grid, 60, y, Width - 15, y);
            g.DrawString((max * (4 - i) / 4).ToString("N0"), Font, ink, 4, y - 7);
        }
        var step = width / (float)Values.Count;
        using var grossBrush = new SolidBrush(Color.FromArgb(46, 111, 168));
        using var netBrush = new SolidBrush(Color.FromArgb(107, 173, 171));
        var every = Math.Max(1, Values.Count / 12);
        for (var i = 0; i < Values.Count; i++)
        {
            var h = height * Values[i].Gross / (float)max;
            var x = 60 + i * step;
            g.FillRectangle(grossBrush, x + 2, 20 + height - h, Math.Max(2, step * .62f), h);
            var nh = height * Math.Max(0, Values[i].Net) / (float)max;
            g.FillRectangle(netBrush, x + step * .65f, 20 + height - nh, Math.Max(2, step * .25f), nh);
            if (i % every == 0) g.DrawString(Values[i].Label, Font, ink, x, 25 + height);
        }
        g.DrawString("Синий — напечатано   •   бирюзовый — после исправлений ≈", Font, ink, 60, Height - 27);
    }
}

internal sealed class Heatmap : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<MetricRow> Rows { get; set; } = [];
    public Heatmap() { DoubleBuffered = true; BackColor = Color.White; Font = new Font("Segoe UI", 9); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var totals = new long[7, 24];
        foreach (var r in Rows)
        {
            if (r.Key.Hour < 0 || !DateTime.TryParse(r.Key.LocalDate, out var d)) continue;
            var day = ((int)d.DayOfWeek + 6) % 7; totals[day, r.Key.Hour] += r.Counts.Gross;
        }
        var max = Math.Max(1, totals.Cast<long>().Max()); var cell = Math.Max(9, (Width - 80) / 24);
        var ch = Math.Max(18, Math.Min(35, (Height - 50) / 7)); var names = new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
        using var ink = new SolidBrush(Color.FromArgb(90, 105, 120));
        for (var hour = 0; hour < 24; hour += 2) e.Graphics.DrawString(hour.ToString("00"), Font, ink, 45 + hour * cell, 4);
        for (var day = 0; day < 7; day++)
        {
            e.Graphics.DrawString(names[day], Font, ink, 9, 28 + day * ch);
            for (var hour = 0; hour < 24; hour++)
            {
                var v = Math.Sqrt(totals[day, hour] / (double)max);
                using var brush = new SolidBrush(Color.FromArgb((int)(236 - v * 190), (int)(243 - v * 132), (int)(247 - v * 80)));
                e.Graphics.FillRectangle(brush, 45 + hour * cell, 25 + day * ch, cell - 3, ch - 3);
            }
        }
    }
}
