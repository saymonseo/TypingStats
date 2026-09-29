using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TypingStats.App.UI;

internal static class Theme
{
    public static readonly Color Page = Color.FromArgb(245, 247, 250);
    public static readonly Color Card = Color.White;
    public static readonly Color Ink = Color.FromArgb(25, 40, 61);
    public static readonly Color Muted = Color.FromArgb(104, 118, 137);
    public static readonly Color Line = Color.FromArgb(226, 232, 240);
    public static readonly Color Blue = Color.FromArgb(43, 104, 216);
    public static readonly Color BlueSoft = Color.FromArgb(235, 242, 255);
    public static readonly Color Green = Color.FromArgb(22, 143, 130);
    public static readonly float Scale = Math.Max(1f, GetDpiForSystem() / 96f);
    public static int P(float value) => (int)Math.Round(value * Scale);
    public static Font Font(float size = 13, bool bold = false, bool display = false) =>
        new(display ? "Bahnschrift" : "Segoe UI", size * Scale, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
    public static Label Label(string text, float size = 13, Color? color = null, bool bold = false) =>
        new() { Text = text, AutoSize = false, Font = Font(size, bold), ForeColor = color ?? Ink, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath(); var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
    public static void Window(Form form)
    {
        form.Font = Font(); form.BackColor = Page; form.ForeColor = Ink; form.AutoScaleMode = AutoScaleMode.None;
        form.HandleCreated += (_, _) =>
        {
            var caption = ColorTranslator.ToWin32(Page); var text = ColorTranslator.ToWin32(Ink); var border = ColorTranslator.ToWin32(Line); var rounded = 2;
            DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int)); DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
            DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int)); DwmSetWindowAttribute(form.Handle, 33, ref rounded, sizeof(int));
        };
    }
    public static void Menu(ContextMenuStrip menu)
    { menu.ShowImageMargin = false; menu.Font = Font(12); menu.ForeColor = Ink; menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors()); }
    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Card;
        public override Color MenuBorder => Line;
        public override Color MenuItemSelected => BlueSoft;
        public override Color MenuItemBorder => BlueSoft;
        public override Color SeparatorDark => Line;
        public override Color SeparatorLight => Card;
    }
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}

internal sealed class CardPanel : Panel
{
    public CardPanel() { DoubleBuffered = true; BackColor = Theme.Card; Padding = new Padding(Theme.P(18)); }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e); if (Width < 2 || Height < 2) return;
        using var path = Theme.Round(new RectangleF(0, 0, Width, Height), Theme.P(12));
        var old = Region; Region = new Region(path); old?.Dispose();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), Theme.P(12));
        using var pen = new Pen(Theme.Line); e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class ModernButton : Button
{
    private bool hovered;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Primary { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Navigation { get; set; }
    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
        Font = Theme.Font(); Height = Theme.P(36); Width = Theme.P(116); Cursor = Cursors.Hand;
        DoubleBuffered = true; Margin = new Padding(0, 0, Theme.P(8), 0);
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Page); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bg = Navigation ? (Selected ? Color.FromArgb(44, 65, 91) : hovered ? Color.FromArgb(34, 51, 75) : Theme.Ink)
            : Primary ? (hovered ? Color.FromArgb(35, 87, 183) : Theme.Blue) : hovered ? Theme.BlueSoft : Theme.Card;
        var fg = Navigation ? (Selected ? Color.White : Color.FromArgb(177, 191, 211)) : Primary ? Color.White : Theme.Ink;
        using var shape = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), Theme.P(8));
        using var fill = new SolidBrush(bg); e.Graphics.FillPath(fill, shape);
        if (!Navigation && !Primary) { using var pen = new Pen(Theme.Line); e.Graphics.DrawPath(pen, shape); }
        if (Navigation && Selected) { using var blue = new SolidBrush(Color.FromArgb(112, 166, 255)); e.Graphics.FillRectangle(blue, Theme.P(4), Theme.P(13), Theme.P(3), Height - Theme.P(26)); }
        var bounds = new Rectangle(Navigation ? Theme.P(20) : Theme.P(8), 0, Width - Theme.P(16), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, fg, TextFormatFlags.VerticalCenter | (Navigation ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.EndEllipsis);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -Theme.P(5), -Theme.P(5)), fg, bg);
    }
}
