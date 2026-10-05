using System.Data;
using System.Globalization;
using TypingStats.Core;
using TypingStats.Storage;
using ApplicationIdentity = TypingStats.Core.ApplicationIdentity;

namespace TypingStats.App.UI;

internal sealed class MouseStatisticsPage : UserControl
{
    private readonly ComboBox application = Choice(), button = Choice(), format = Choice();
    private readonly CheckBox emptyDays = new() { Text = "Дни без записей", Font = Theme.Font(11), AutoSize = true, ForeColor = Theme.Muted };
    private readonly DataGridView days = StatisticsPage.Table(), buttons = StatisticsPage.Table(), apps = StatisticsPage.Table();
    private readonly Label[] figures = new Label[4];
    private readonly Label title = Theme.Label("Кнопки за весь период", 16, bold: true), note = Theme.Label("", 11, Theme.Muted);
    private readonly Label status = Theme.Label("", 11, Theme.Muted);
    private readonly ModernButton exportPeriod = new() { Text = "Экспорт периода", Primary = true, Width = Theme.P(155) };
    private readonly ModernButton exportDay = new() { Text = "Экспорт дня", Width = Theme.P(125) };
    private MouseMetricRow[] source = [];
    private IReadOnlyDictionary<string, ApplicationIdentity> names = new Dictionary<string, ApplicationIdentity>();
    private MouseStatisticsReport? report;
    private DateOnly from, to;
    private DateOnly? selectedDay;
    private bool binding, valid;
    private sealed record AppChoice(string? Id, string Caption) { public override string ToString() => Caption; }
    public MouseStatisticsPage()
    {
        Dock = DockStyle.Fill; BackColor = Theme.Card;
        buttons.RowTemplate.Height = Theme.P(28); buttons.ColumnHeadersHeight = Theme.P(28); buttons.Font = Theme.Font(11);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(67)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(80)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(55)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(25))); Controls.Add(root);
        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        application.Items.Add(new AppChoice(null, "Все приложения")); application.SelectedIndex = 0;
        button.Items.Add("Все кнопки"); button.Items.AddRange(Enum.GetValues<TypingStats.Core.MouseButton>().Select(b => (object)MouseStatistics.Label(b)).ToArray()); button.SelectedIndex = 0;
        filters.Controls.Add(StatisticsPage.FilterBox("Приложение", application), 0, 0);
        filters.Controls.Add(StatisticsPage.FilterBox("Кнопка мыши", button), 1, 0);
        var reset = new ModernButton { Text = "Сбросить", Dock = DockStyle.Bottom, Margin = new Padding(0, 0, 0, Theme.P(10)) }; filters.Controls.Add(reset, 2, 0); root.Controls.Add(filters, 0, 0);
        var summary = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0, 0, 0, Theme.P(10)) };
        var captions = new[] { "Нажатий мыши", "Кнопок использовано", "Дней с записями", "Приложений" };
        for (var i = 0; i < 4; i++)
        {
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Page, Padding = new Padding(Theme.P(12), Theme.P(7), Theme.P(12), Theme.P(7)), Margin = new Padding(0, 0, Theme.P(8), 0) };
            var caption = Theme.Label(captions[i], 10, Theme.Muted); caption.Dock = DockStyle.Top; caption.Height = Theme.P(20);
            figures[i] = Theme.Label("—", 25, i == 0 ? Theme.Blue : Theme.Ink, true); figures[i].Font = Theme.Font(25, true, true); figures[i].Dock = DockStyle.Bottom; figures[i].Height = Theme.P(36);
            panel.Controls.AddRange([figures[i], caption]); summary.Controls.Add(panel, i, 0);
        }
        root.Controls.Add(summary, 0, 1);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 67));
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, Theme.P(12), 0) };
        var dayHeader = new Panel { Dock = DockStyle.Top, Height = Theme.P(40) };
        var dayTitle = Theme.Label("История по дням", 15, bold: true); dayTitle.Dock = DockStyle.Left; dayTitle.Width = Theme.P(155);
        emptyDays.Dock = DockStyle.Right; dayHeader.Controls.AddRange([dayTitle, emptyDays]); left.Controls.Add(days); left.Controls.Add(dayHeader);
        var right = new Panel { Dock = DockStyle.Fill };
        var detailHeader = new Panel { Dock = DockStyle.Top, Height = Theme.P(62) };
        title.Dock = DockStyle.Top; title.Height = Theme.P(29); note.Dock = DockStyle.Bottom; note.Height = Theme.P(29); detailHeader.Controls.AddRange([title, note]);
        var detail = new Panel { Dock=DockStyle.Fill,BackColor=Theme.Card };
        var switcher = new FlowLayoutPanel {Dock=DockStyle.Top,Height=Theme.P(39),WrapContents=false};
        var byButton = new ModernButton {Text="Кнопки",Primary=true,Width=Theme.P(110)};
        var byApp = new ModernButton {Text="Приложения",Width=Theme.P(140)};
        switcher.Controls.AddRange([byButton,byApp]);
        var tableHost = new Panel {Dock=DockStyle.Fill,BackColor=Theme.Card};tableHost.Controls.AddRange([buttons,apps]);apps.Visible=false;buttons.BringToFront();
        byButton.Click+=(_,_)=>{buttons.Visible=true;apps.Visible=false;byButton.Primary=true;byApp.Primary=false;byButton.Invalidate();byApp.Invalidate();};
        byApp.Click+=(_,_)=>{apps.Visible=true;buttons.Visible=false;byButton.Primary=false;byApp.Primary=true;byButton.Invalidate();byApp.Invalidate();};
        detail.Controls.Add(tableHost);detail.Controls.Add(switcher);right.Controls.Add(detail);right.Controls.Add(detailHeader);
        body.Controls.Add(left, 0, 0); body.Controls.Add(right, 1, 0); root.Controls.Add(body, 0, 2);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty, Padding = new Padding(0, Theme.P(8), 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.P(390)));
        format.Items.AddRange(["CSV: день → приложение → кнопка", "CSV: сводка по дням", "CSV: суммы по кнопкам"]); format.SelectedIndex = 0;
        footer.Controls.Add(format, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.RightToLeft };
        var all = new ModernButton { Text = "Все дни", Width = Theme.P(95) }; actions.Controls.AddRange([exportPeriod, exportDay, all]); footer.Controls.Add(actions, 1, 0); root.Controls.Add(footer, 0, 3);
        status.Dock = DockStyle.Fill; root.Controls.Add(status, 0, 4);
        application.SelectedIndexChanged += (_, _) => { if (!binding) Rebuild(); };
        button.SelectedIndexChanged += (_, _) => { if (!binding) Rebuild(); };
        emptyDays.CheckedChanged += (_, _) => { if (!binding) Rebuild(); };
        reset.Click += (_, _) => { binding = true; application.SelectedIndex = button.SelectedIndex = 0; emptyDays.Checked = false; selectedDay = null; binding = false; Rebuild(); };
        days.SelectionChanged += (_, _) =>
        {
            if (binding) return;
            if (days.CurrentRow?.Selected == true && days.CurrentRow.Cells[0].Value is DateTime date) { selectedDay = DateOnly.FromDateTime(date); Details(); }
        };
        buttons.CellPainting += PaintShare;
        all.Click += (_, _) => { selectedDay = null; days.ClearSelection(); Details(); };
        exportPeriod.Click += (_, _) => ExportPeriod(); exportDay.Click += (_, _) => Export(true);
        exportPeriod.Enabled = exportDay.Enabled = false;
    }
    private static ComboBox Choice() => new ThemedComboBox() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = Theme.Font(11) };
    private string Caption(string id) => names.TryGetValue(id, out var app) ? app.Caption : ApplicationIdentity.Unknown(id).Caption;
    public void SetSource(IReadOnlyList<MouseMetricRow> rows, DateOnly begin, DateOnly end, IReadOnlyDictionary<string, ApplicationIdentity> appNames, bool enabled)
    {
        var selected = (application.SelectedItem as AppChoice)?.Id; names = appNames;
        source = rows.ToArray(); from = begin; to = end; valid = begin <= end;
        binding = true; application.Items.Clear(); application.Items.Add(new AppChoice(null, "Все приложения"));
        var ids = rows.Select(r => r.App).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (selected != null && !ids.Contains(selected, StringComparer.OrdinalIgnoreCase)) ids.Add(selected);
        application.Items.AddRange(ids.OrderBy(Caption, StringComparer.CurrentCultureIgnoreCase).Select(id => (object)new AppChoice(id, Caption(id))).ToArray());
        application.SelectedIndex = selected == null ? 0 : application.Items.Cast<AppChoice>().ToList().FindIndex(a => a.Id == selected);
        binding = false; Rebuild();
        status.Text = (enabled ? "Учёт мыши включён" : "Учёт мыши выключен; история сохранена") + " · Двойной клик = 2 нажатия · Координаты не сохраняются";
    }
    public void InvalidPeriod()
    {
        valid = false; report = null; days.DataSource = buttons.DataSource = apps.DataSource = null;
        foreach (var figure in figures) figure.Text = "—";
        exportPeriod.Enabled = exportDay.Enabled = false; status.Text = "Проверьте даты периода";
    }
    private void Rebuild()
    {
        if (!valid) return;
        report = MouseStatistics.Build(source, new MouseStatisticsFilter(from, to, (application.SelectedItem as AppChoice)?.Id,
            button.SelectedIndex <= 0 ? null : (TypingStats.Core.MouseButton)button.SelectedIndex, emptyDays.Checked));
        figures[0].Text = report.Presses.ToString("N0"); figures[1].Text = report.UsedButtons.ToString("N0"); figures[2].Text = report.DataDays.ToString("N0"); figures[3].Text = report.Applications.ToString("N0");
        if (selectedDay != null && !report.Days.Any(d => d.Date == selectedDay)) selectedDay = null;
        var table = new DataTable(); table.Columns.Add("date", typeof(DateTime)); table.Columns.Add("presses", typeof(long));
        foreach (var d in report.Days) table.Rows.Add(d.Date.ToDateTime(TimeOnly.MinValue), d.Presses);
        binding = true; days.DataSource = table.DefaultView; Theme.Table(days); days.Columns[0].HeaderText = "День"; days.Columns[0].DefaultCellStyle.Format = "dd.MM.yyyy";
        days.Columns[1].HeaderText = "Нажатий"; days.Columns[1].DefaultCellStyle.Format = "N0"; days.ClearSelection();
        if (selectedDay != null) foreach (DataGridViewRow row in days.Rows)
            if (row.Cells[0].Value is DateTime d && DateOnly.FromDateTime(d) == selectedDay) { days.CurrentCell = row.Cells[0]; row.Selected = true; break; }
        binding = false; exportPeriod.Enabled = true; Details();
    }
    private void Details()
    {
        if (report == null) return;
        var detail = report.Breakdown(selectedDay); var table = new DataTable();
        table.Columns.Add("button", typeof(string)); table.Columns.Add("presses", typeof(long)); table.Columns.Add("share", typeof(double)); table.Columns.Add("injected", typeof(long));
        foreach (var b in detail) table.Rows.Add(b.Label, b.Presses, b.Share, b.Injected);
        buttons.DataSource = table.DefaultView;
        Theme.Table(buttons);
        foreach (DataGridViewColumn col in buttons.Columns)
        {
            col.HeaderText = col.DataPropertyName switch { "button" => "Кнопка", "presses" => "Нажатий", "share" => "Доля, %", _ => "Программные" };
            col.DefaultCellStyle.Format = col.DataPropertyName == "share" ? "N2" : "N0";
            if (col.DataPropertyName == "button") { col.FillWeight = 180; col.DefaultCellStyle.Format = ""; }
        }
        var rows = report.ForDay(selectedDay); var appTable = new DataTable(); appTable.Columns.Add("Приложение", typeof(string)); appTable.Columns.Add("Всего", typeof(long));
        foreach (var b in Enum.GetValues<TypingStats.Core.MouseButton>()) appTable.Columns.Add(b.ToString(), typeof(long));
        foreach (var g in rows.GroupBy(r => r.App).OrderByDescending(g => g.Sum(r => r.Presses)))
            appTable.Rows.Add(new object[] { Caption(g.Key), g.Sum(r => r.Presses) }.Concat(Enum.GetValues<TypingStats.Core.MouseButton>().Select(b => (object)g.Where(r => r.Button == b).Sum(r => r.Presses))).ToArray());
        apps.DataSource = appTable.DefaultView;
        Theme.Table(apps);
        apps.Columns[0].FillWeight = 250;
        for (var i = 1; i < apps.Columns.Count; i++) { apps.Columns[i].DefaultCellStyle.Format = "N0"; if (i >= 2) apps.Columns[i].HeaderText = new[] { "ЛКМ", "ПКМ", "Средняя", "X1", "X2" }[i - 2]; }
        title.Text = selectedDay?.ToString("d MMMM yyyy, ddd", CultureInfo.GetCultureInfo("ru-RU")) ?? "Мышь за весь период";
        note.Text = rows.Count == 0 ? "Нет записей: сбор начинается после обновления" : $"{rows.Sum(r => r.Presses):N0} нажатий · {rows.Select(r => r.App).Distinct().Count():N0} приложений";
        exportDay.Enabled = selectedDay != null;
    }
    private static void PaintShare(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex].DataPropertyName != "share") return;
        e.PaintBackground(e.ClipBounds, true);
        var width = (int)((e.CellBounds.Width - Theme.P(10)) * Math.Clamp(Convert.ToDouble(e.Value), 0, 100) / 100);
        using var brush = new SolidBrush(Theme.BlueSoft); e.Graphics!.FillRectangle(brush, e.CellBounds.X + Theme.P(4), e.CellBounds.Y + Theme.P(9), width, e.CellBounds.Height - Theme.P(18));
        e.PaintContent(e.ClipBounds); e.Handled = true;
    }
    public void ExportPeriod() => Export(false);
    private void Export(bool dayOnly)
    {
        if (report == null || (dayOnly && selectedDay == null)) return;
        var snapshot = report; var day = dayOnly ? selectedDay : null; var export = (MouseStatisticsExport)format.SelectedIndex;
        using var file = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "typingstats-mouse-" + (day?.ToString("yyyyMMdd") ?? from.ToString("yyyyMMdd") + "-" + to.ToString("yyyyMMdd")) + ".csv" };
        if (file.ShowDialog(FindForm()) != DialogResult.OK) return;
        try { MouseStatisticsCsv.Write(snapshot, file.FileName, export, day, names); MessageBox.Show(FindForm(), "Статистика мыши сохранена. Применены текущие фильтры.", "TypingStats"); }
        catch (Exception e) { MessageBox.Show(FindForm(), e.Message, "TypingStats", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    public void VerifyExport(string directory)
    {
        if (report == null) throw new InvalidOperationException("Mouse report not initialized");
        MouseStatisticsCsv.Write(report, Path.Combine(directory, "mouse-period.csv"), MouseStatisticsExport.Detailed, applications: names);
        MouseStatisticsCsv.Write(report, Path.Combine(directory, "mouse-days.csv"), MouseStatisticsExport.Daily);
        MouseStatisticsCsv.Write(report, Path.Combine(directory, "mouse-buttons.csv"), MouseStatisticsExport.Buttons);
        selectedDay = report.Days.FirstOrDefault(d => d.Presses > 0)?.Date; Details();
        if (report.Breakdown(selectedDay).Sum(b => b.Presses) != report.ForDay(selectedDay).Sum(r => r.Presses)) throw new InvalidOperationException("Mouse view totals mismatch");
    }
}
