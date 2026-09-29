using System.Drawing.Drawing2D;

namespace TypingStats.App.UI;

internal sealed class ToggleSwitch : CheckBox
{
    public ToggleSwitch()
    {
        AutoSize = false; Height = Theme.P(44); Font = Theme.Font(); Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Card); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width - Theme.P(65), Height), Theme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        var r = new RectangleF(Width - Theme.P(46), (Height - Theme.P(24)) / 2f, Theme.P(42), Theme.P(24));
        using var path = Theme.Round(r, Theme.P(12)); using var track = new SolidBrush(Checked ? Theme.Blue : Color.FromArgb(211, 220, 232)); e.Graphics.FillPath(track, path);
        using var knob = new SolidBrush(Color.White); var x = Checked ? r.Right - Theme.P(21) : r.Left + Theme.P(3);
        e.Graphics.FillEllipse(knob, x, r.Y + Theme.P(3), Theme.P(18), Theme.P(18));
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2));
    }
}
