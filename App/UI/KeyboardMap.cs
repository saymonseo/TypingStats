using System.Drawing.Drawing2D;
using TypingStats.Core;

namespace TypingStats.App.UI;

internal sealed class KeyboardMap : Control
{
    private Dictionary<string, long> totals = new();
    private readonly List<(RectangleF Rect, string Code, string Label)> hits = new();
    private readonly ToolTip tip = new();
    private string? hovered;
    public KeyboardMap() { DoubleBuffered = true; BackColor = Theme.Card; Font = Theme.Font(9); }
    public void SetRows(IEnumerable<KeyMetricRow> rows)
    { totals = rows.GroupBy(r => r.Code).ToDictionary(g => g.Key, g => g.Sum(r => r.Presses)); Invalidate(); }
    private static (string Label, int Scan, float Units) K(string label, int scan, float units = 1) => (label, scan, units);
    private static readonly (string Label, int Scan, float Units)[][] KeyLayout =
    [
        [K("Esc",1),K("F1",0x3b),K("F2",0x3c),K("F3",0x3d),K("F4",0x3e),K("F5",0x3f),K("F6",0x40),K("F7",0x41),K("F8",0x42),K("F9",0x43),K("F10",0x44),K("F11",0x57),K("F12",0x58),K("Prt",0xe037),K("Del",0xe053)],
        [K("~",0x29),K("1",2),K("2",3),K("3",4),K("4",5),K("5",6),K("6",7),K("7",8),K("8",9),K("9",10),K("0",11),K("-",12),K("=",13),K("Backspace",14,2)],
        [K("Tab",15,1.5f),K("Q",0x10),K("W",0x11),K("E",0x12),K("R",0x13),K("T",0x14),K("Y",0x15),K("U",0x16),K("I",0x17),K("O",0x18),K("P",0x19),K("[",0x1a),K("]",0x1b),K("\\",0x2b,1.5f)],
        [K("Caps",0x3a,1.75f),K("A",0x1e),K("S",0x1f),K("D",0x20),K("F",0x21),K("G",0x22),K("H",0x23),K("J",0x24),K("K",0x25),K("L",0x26),K(";",0x27),K("'",0x28),K("Enter",0x1c,2.25f)],
        [K("Shift",0x2a,2.25f),K("Z",0x2c),K("X",0x2d),K("C",0x2e),K("V",0x2f),K("B",0x30),K("N",0x31),K("M",0x32),K(",",0x33),K(".",0x34),K("/",0x35),K("Shift",0x36,1.75f),K("↑",0xe048)],
        [K("Ctrl",0x1d,1.25f),K("Win",0xe05b,1.25f),K("Alt",0x38,1.25f),K("Space",0x39,4.75f),K("Alt",0xe038,1.25f),K("Menu",0xe05d),K("Ctrl",0xe01d,1.25f),K("←",0xe04b),K("↓",0xe050),K("→",0xe04d)]
    ];
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; hits.Clear();
        var unit = Math.Max(5, Math.Min((Width - Theme.P(8)) / 15f, (Height - Theme.P(10)) / 6f));
        var left = (Width - unit * 15) / 2; var max = Math.Max(1, totals.Values.DefaultIfEmpty(0).Max());
        for (var row = 0; row < KeyLayout.Length; row++)
        {
            var x = left;
            foreach (var k in KeyLayout[row])
            {
                var code = "sc:" + k.Scan.ToString("X4"); var value = totals.GetValueOrDefault(code);
                var strength = Math.Sqrt(value / (double)max);
                var r = new RectangleF(x + Theme.P(2), row * unit + Theme.P(2), k.Units * unit - Theme.P(4), unit - Theme.P(4));
                using var p = Theme.Round(r, Theme.P(5));
                using var fill = new SolidBrush(Color.FromArgb((int)(240 - strength * 197), (int)(244 - strength * 140), (int)(251 - strength * 35))); e.Graphics.FillPath(fill, p);
                using var pen = new Pen(value > 0 ? Color.FromArgb(160, 190, 235) : Theme.Line); e.Graphics.DrawPath(pen, p);
                using var ink = new SolidBrush(strength > .55 ? Color.White : Theme.Ink);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                e.Graphics.DrawString(k.Label, Font, ink, new RectangleF(r.X, r.Y, r.Width, r.Height * .56f), format);
                e.Graphics.DrawString(value == 0 ? "·" : value.ToString("N0"), Font, ink, new RectangleF(r.X, r.Y + r.Height * .48f, r.Width, r.Height * .5f), format);
                hits.Add((r, code, k.Label)); x += k.Units * unit;
            }
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); var hit = hits.FirstOrDefault(h => h.Rect.Contains(e.Location));
        if (hit.Code == hovered) return; hovered = hit.Code;
        tip.SetToolTip(this, hit.Code == null ? "" : hit.Label + ": " + totals.GetValueOrDefault(hit.Code).ToString("N0") + " нажатий");
    }
    protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
}
