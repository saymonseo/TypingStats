using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using TypingStats.Core;
using Microsoft.Win32;

namespace TypingStats.App.UI;

internal static class Theme
{
    private static ThemePalette palette = ThemePalette.Light;
    public static bool IsDark { get; private set; }
    public static AppTheme Preference { get; private set; }
    public static Color Page => palette.Page;
    public static Color Card => palette.Card;
    public static Color Ink => palette.Ink;
    public static Color Muted => palette.Muted;
    public static Color Line => palette.Line;
    public static Color Blue => palette.Blue;
    public static Color BlueSoft => palette.BlueSoft;
    public static Color Green => palette.Green;
    public static Color Sidebar => palette.Sidebar;
    public static Color ToggleOff => palette.ToggleOff;
    public static Color Primary => Color.FromArgb(43,104,216);
    public static Color Blend(Color a, Color b, double amount) => Color.FromArgb((int)(a.R+(b.R-a.R)*amount), (int)(a.G+(b.G-a.G)*amount), (int)(a.B+(b.B-a.B)*amount));
    private enum Role { Fixed, Page, Card, Ink, Muted, Line, Blue, BlueSoft, Sidebar, Transparent }
    private sealed record Binding(Role Background, Color OriginalBackground, Role Foreground, Color OriginalForeground);
    private static readonly ConditionalWeakTable<Control, Binding> bindings = new();
    public static event Action? Changed;
    private static bool SystemDark()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return key?.GetValue("AppsUseLightTheme") is int value && value == 0; }
        catch (Exception e) when(e is System.Security.SecurityException or UnauthorizedAccessException) { return false; }
    }
    public static void Set(AppTheme preference)
    {
        var dark = ThemePalette.IsDark(preference, SystemDark()); Preference = preference;
        if (IsDark == dark && Application.OpenForms.Count > 0) return;
        foreach (Form form in Application.OpenForms) Capture(form);
        IsDark = dark; palette = dark ? ThemePalette.Dark : ThemePalette.Light;
        Application.SetColorMode(dark ? SystemColorMode.Dark : SystemColorMode.Classic);
        foreach (Form form in Application.OpenForms) Apply(form);
        Changed?.Invoke();
    }
    private static Role Identify(Color color)
    {
        if (color.A == 0) return Role.Transparent;
        if (color.ToArgb() == Page.ToArgb()) return Role.Page;
        if (color.ToArgb() == Card.ToArgb() || color.ToArgb() == Color.White.ToArgb()) return Role.Card;
        if (color.ToArgb() == Ink.ToArgb()) return Role.Ink;
        if (color.ToArgb() == Muted.ToArgb()) return Role.Muted;
        if (color.ToArgb() == Line.ToArgb()) return Role.Line;
        if (color.ToArgb() == Blue.ToArgb()) return Role.Blue;
        if (color.ToArgb() == BlueSoft.ToArgb()) return Role.BlueSoft;
        return Role.Fixed;
    }
    private static Color Resolve(Role role, Color fallback) => role switch
    { Role.Page => Page, Role.Card => Card, Role.Ink => Ink, Role.Muted => Muted, Role.Line => Line, Role.Blue => Blue, Role.BlueSoft => BlueSoft, Role.Sidebar => Sidebar, Role.Transparent => Color.Transparent, _ => fallback };
    public static void Capture(Control root, bool side = false)
    {
        side |= root.Name == "Sidebar";
        if (!bindings.TryGetValue(root, out _)) bindings.Add(root, new Binding(side ? Role.Sidebar : Identify(root.BackColor), root.BackColor,
            side ? Role.Fixed : Identify(root.ForeColor), root.ForeColor));
        foreach (Control child in root.Controls) Capture(child, side);
    }
    public static void Apply(Control root, bool side = false)
    {
        side |= root.Name == "Sidebar"; Capture(root, side);
        var binding = bindings.GetValue(root, _ => throw new InvalidOperationException());
        if (root.IsHandleCreated && root is TextBoxBase or ComboBox or NumericUpDown or DateTimePicker)
            SetWindowTheme(root.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);
        if (root is not ModernButton)
        {
            var background = Resolve(binding.Background, binding.OriginalBackground);
            if (binding.Background == Role.Fixed && root is not System.Windows.Forms.Label) background = side ? Sidebar : Card;
            root.BackColor = background;
            root.ForeColor = Resolve(binding.Foreground, binding.OriginalForeground);
        }
        if (root is TextBoxBase or ComboBox or NumericUpDown or DateTimePicker)
        { root.BackColor = Card; root.ForeColor = Ink; }
        if (root is DateTimePicker picker) { picker.CalendarMonthBackground = Card; picker.CalendarForeColor = Ink; picker.CalendarTitleBackColor = BlueSoft; picker.CalendarTitleForeColor = Ink; }
        if (root is DataGridView grid)
            Table(grid);
        if (root is TabControl tabs && tabs.DrawMode != TabDrawMode.OwnerDrawFixed)
        {
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.DrawItem += (_, e) =>
            {
                using var fill = new SolidBrush(e.Index == tabs.SelectedIndex ? BlueSoft : Card); e.Graphics.FillRectangle(fill, e.Bounds);
                TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
        }
        if (root.IsHandleCreated)
        {
            if (root is Form form) Caption(form);
        }
        foreach (Control child in root.Controls) Apply(child, side);
        // Inherited ForeColor changes on WinForms child controls can rewrite cell styles.
        // Reapply the semantic table palette after the entire child tree has settled.
        if(root is DataGridView finalGrid)Table(finalGrid);
        root.Invalidate(true);
    }
    public static void Table(DataGridView grid)
    {
        grid.ForeColor=Ink;grid.BackColor=Card;grid.BackgroundColor = Card; grid.GridColor = Line;
        DataGridViewCellStyle Styled(DataGridViewCellStyle source, bool header=false)
        {
            if(source.BackColor.ToArgb()==(header?Page:Card).ToArgb() && source.ForeColor.ToArgb()==(header?Muted:Ink).ToArgb()
                && source.SelectionBackColor.ToArgb()==BlueSoft.ToArgb() && source.SelectionForeColor.ToArgb()==Ink.ToArgb())return source;
            var style=new DataGridViewCellStyle(source){BackColor=header?Page:Card,ForeColor=header?Muted:Ink,SelectionBackColor=BlueSoft,SelectionForeColor=Ink};return style;
        }
        grid.DefaultCellStyle=Styled(grid.DefaultCellStyle);
        grid.RowsDefaultCellStyle=Styled(grid.RowsDefaultCellStyle);
        grid.AlternatingRowsDefaultCellStyle=Styled(grid.AlternatingRowsDefaultCellStyle);
        grid.ColumnHeadersDefaultCellStyle=Styled(grid.ColumnHeadersDefaultCellStyle,true);
    }
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
        form.HandleCreated += (_, _) => Caption(form);
        form.Shown += (_, _) => Apply(form);
    }
    private static void Caption(Form form)
    {
        var caption = ColorTranslator.ToWin32(Page); var text = ColorTranslator.ToWin32(Ink); var border = ColorTranslator.ToWin32(Line); var rounded = 2; var dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)); DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
        DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int)); DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int)); DwmSetWindowAttribute(form.Handle, 33, ref rounded, sizeof(int));
    }
    public static void Menu(ContextMenuStrip menu)
    { menu.ShowImageMargin = false; menu.Font = Font(12); menu.ForeColor = Ink; menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
      menu.Opening += (_, _) => { menu.BackColor = Card; menu.ForeColor = Ink; foreach(ToolStripItem item in menu.Items) item.ForeColor = Ink; }; }
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
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(nint hwnd, string? appName, string? idList);
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
        var bg = Navigation ? (Selected ? Color.FromArgb(44, 65, 91) : hovered ? Color.FromArgb(34, 51, 75) : Theme.Sidebar)
            : Primary ? (hovered ? Color.FromArgb(35, 87, 183) : Theme.Primary) : hovered ? Theme.BlueSoft : Theme.Card;
        var fg = !Enabled ? Theme.Muted : Navigation ? (Selected ? Color.White : Color.FromArgb(177, 191, 211)) : Primary ? Color.White : Theme.Ink;
        using var shape = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), Theme.P(8));
        using var fill = new SolidBrush(bg); e.Graphics.FillPath(fill, shape);
        if (!Navigation && !Primary) { using var pen = new Pen(Theme.Line); e.Graphics.DrawPath(pen, shape); }
        if (Navigation && Selected) { using var blue = new SolidBrush(Color.FromArgb(112, 166, 255)); e.Graphics.FillRectangle(blue, Theme.P(4), Theme.P(13), Theme.P(3), Height - Theme.P(26)); }
        var bounds = new Rectangle(Navigation ? Theme.P(20) : Theme.P(8), 0, Width - Theme.P(16), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, fg, TextFormatFlags.VerticalCenter | (Navigation ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.EndEllipsis);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -Theme.P(5), -Theme.P(5)), fg, bg);
    }
}
