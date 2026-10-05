using System.Drawing.Drawing2D;
using TypingStats.Core;

namespace TypingStats.App.UI;

internal sealed class HistoryChart : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<(string Label, long Gross, long Net)> Values { get; set; } = [];
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool KeysMode { get; set; }
    public HistoryChart() { DoubleBuffered = true; BackColor = Theme.Card; Font = Theme.Font(11); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using var ink = new SolidBrush(Theme.Muted);
        if (Values.Count == 0 || Values.All(v => v.Gross == 0 && v.Net == 0))
        {
            var cx = Width / 2f; var cy = Height / 2f - Theme.P(30);
            using var blue = new SolidBrush(Theme.BlueSoft);
            for (var i = 0; i < 3; i++) { var h = Theme.P(20 + i * 14); using var p = Theme.Round(new RectangleF(cx - Theme.P(29) + i * Theme.P(22), cy - h, Theme.P(16), h), Theme.P(4)); g.FillPath(blue, p); }
            using var heading = Theme.Font(16, true); using var format = new StringFormat { Alignment = StringAlignment.Center };
            g.DrawString(KeysMode ? "Пока нет нажатий" : "Пока нет набора текста", heading, ink, new RectangleF(0, cy + Theme.P(18), Width, Theme.P(30)), format);
            g.DrawString("Начните печатать — здесь появится ваша активность", Font, ink, new RectangleF(0, cy + Theme.P(52), Width, Theme.P(25)), format); return;
        }
        var left = Theme.P(52); var top = Theme.P(18); var width = Math.Max(1, Width - left - Theme.P(12)); var height = Math.Max(1, Height - Theme.P(83));
        var max = Math.Max(1L, Values.Max(v => v.Gross)); var min = Math.Min(0L, Values.Min(v => v.Net));
        float Y(long value) => top + height * (max - value) / (float)(max - min);
        var baseline = Y(0);
        using var grid = new Pen(Theme.Line);
        for (var i = 0; i <= 4; i++)
        {
            var value = max - (max - min) * i / 4; var y = top + height * i / 4f;
            g.DrawLine(grid, left, y, Width - Theme.P(10), y); g.DrawString(value.ToString("N0"), Font, ink, 0, y - Theme.P(7));
        }
        var step = width / (float)Values.Count;
        using var grossBrush = new SolidBrush(Theme.Blue); using var netBrush = new SolidBrush(Theme.Green); using var negativeBrush = new SolidBrush(Color.FromArgb(211, 146, 55));
        var every = Math.Max(1, (int)Math.Ceiling(Values.Count / 12.0));
        for (var i = 0; i < Values.Count; i++)
        {
            var x = left + i * step; var w = Math.Max(1, step * .5f);
            var gy = Y(Values[i].Gross); var gh = baseline - gy;
            if (gh > .5f) { using var p = Theme.Round(new RectangleF(x + 2, gy, w, gh), Theme.P(3)); g.FillPath(grossBrush, p); }
            var ny = Y(Values[i].Net); var nh = Math.Abs(baseline - ny);
            if (nh > .5f) { using var p = Theme.Round(new RectangleF(x + step * .56f, Math.Min(ny, baseline), Math.Max(1, step * .22f), nh), Theme.P(2)); g.FillPath(Values[i].Net >= 0 ? netBrush : negativeBrush, p); }
            if (i % every == 0) g.DrawString(Values[i].Label, Font, ink, x, top + height + Theme.P(12));
        }
        var legendY = Height - Theme.P(20);
        g.FillEllipse(grossBrush, left, legendY + Theme.P(3), Theme.P(7), Theme.P(7)); g.DrawString(KeysMode ? "Нажатия" : "Напечатано", Font, ink, left + Theme.P(14), legendY);
        if (!KeysMode) { g.FillEllipse(netBrush, left + Theme.P(122), legendY + Theme.P(3), Theme.P(7), Theme.P(7)); g.DrawString("После правок ≈", Font, ink, left + Theme.P(136), legendY); }
    }
}

internal sealed class Heatmap : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<MetricRow> Rows { get; set; } = [];
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool KeysMode { get; set; }
    public Heatmap() { DoubleBuffered = true; BackColor = Theme.Card; Font = Theme.Font(11); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; var totals = new long[7, 24];
        foreach (var r in Rows)
        {
            if (r.Key.Hour < 0 || !DateTime.TryParse(r.Key.LocalDate, out var d)) continue;
            totals[((int)d.DayOfWeek + 6) % 7, r.Key.Hour] += KeysMode ? r.Counts.KeyPresses : r.Counts.Gross;
        }
        var max = Math.Max(1, totals.Cast<long>().Max()); var left = Theme.P(40); var top = Theme.P(36);
        var cell = Math.Max(4, (Width - left - Theme.P(12)) / 24f); var ch = Math.Max(Theme.P(18), Math.Min(Theme.P(40), (Height - Theme.P(70)) / 7f));
        var names = new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
        using var ink = new SolidBrush(Theme.Muted);
        for (var hour = 0; hour < 24; hour += 3) e.Graphics.DrawString(hour.ToString("00"), Font, ink, left + hour * cell, Theme.P(10));
        for (var day = 0; day < 7; day++)
        {
            e.Graphics.DrawString(names[day], Font, ink, 0, top + day * ch + Theme.P(5));
            for (var hour = 0; hour < 24; hour++)
            {
                var v = Math.Sqrt(totals[day, hour] / (double)max);
                using var brush = new SolidBrush(Theme.Blend(Theme.BlueSoft, Theme.Blue, v));
                using var p = Theme.Round(new RectangleF(left + hour * cell, top + day * ch, Math.Max(1, cell - Theme.P(3)), ch - Theme.P(4)), Theme.P(3)); e.Graphics.FillPath(brush, p);
            }
        }
        e.Graphics.DrawString("Меньше", Font, ink, left, top + 7 * ch + Theme.P(12));
        for (var i = 0; i < 5; i++) { using var b = new SolidBrush(Theme.Blend(Theme.BlueSoft, Theme.Blue, i / 4.0)); e.Graphics.FillRectangle(b, left + Theme.P(65 + i * 16), top + 7 * ch + Theme.P(14), Theme.P(12), Theme.P(12)); }
        e.Graphics.DrawString("Больше", Font, ink, left + Theme.P(155), top + 7 * ch + Theme.P(12));
    }
}
