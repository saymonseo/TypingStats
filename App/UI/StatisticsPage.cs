using System.Data;
using System.Globalization;
using TypingStats.Core;
using TypingStats.Storage;
using ApplicationIdentity=TypingStats.Core.ApplicationIdentity;

namespace TypingStats.App.UI;

internal sealed class StatisticsPage : UserControl
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private readonly ComboBox application = Choice(), group = Choice();
    private readonly TextBox search = new() { BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(), PlaceholderText = "W, Ctrl, пробел…", Dock = DockStyle.Fill };
    private readonly CheckBox emptyDays = new() { Text = "Дни без записей", Font = Theme.Font(11), AutoSize = true, ForeColor = Theme.Muted };
    private readonly DataGridView days = Table(), keys = Table();
    private readonly Label[] figures = new Label[4];
    private readonly Label detailTitle = Theme.Label("Выберите день", 16, bold: true);
    private readonly Label detailNote = Theme.Label("", 11, Theme.Muted);
    private readonly Label status = Theme.Label("", 11, Theme.Muted);
    private readonly ModernButton exportPeriod = new() { Text = "Экспорт периода", Primary = true, Width = Theme.P(160) };
    private readonly ModernButton exportDay = new() { Text = "Экспорт дня", Width = Theme.P(130) };
    private KeyMetricRow[] source = [];
    private DateOnly from, to;
    private DateOnly? selectedDay;
    private KeyStatisticsReport? report;
    private bool binding, valid;
    private IReadOnlyDictionary<string,ApplicationIdentity> appNames=new Dictionary<string,ApplicationIdentity>();
    private sealed record AppChoice(string? Id,string Caption){public override string ToString()=>Caption;}
    public StatisticsPage()
    {
        Dock = DockStyle.Fill; BackColor = Theme.Card;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(70)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(83)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(53))); Controls.Add(root);
        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = Padding.Empty };
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12));
        application.Items.Add(new AppChoice(null,"Все приложения")); application.SelectedIndex = 0;
        group.Items.AddRange(["Все клавиши", "Буквы", "Цифры", "Модификаторы", "Навигация", "F-клавиши", "Numpad", "Символы / пробел", "Другие"]); group.SelectedIndex = 0;
        filters.Controls.Add(FilterBox("Приложение", application), 0, 0);
        filters.Controls.Add(FilterBox("Группа клавиш", group), 1, 0);
        filters.Controls.Add(FilterBox("Поиск клавиши", search), 2, 0);
        var reset = new ModernButton { Text = "Сбросить", Dock = DockStyle.Bottom, Margin = new Padding(0, 0, 0, Theme.P(10)) };
        filters.Controls.Add(reset, 3, 0); root.Controls.Add(filters, 0, 0);
        var summary = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0, 0, 0, Theme.P(12)) };
        var titles = new[] { "Нажатий за период", "Разных клавиш", "Дней с записями", "Приложений" };
        for (var i = 0; i < 4; i++)
        {
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Page, Padding = new Padding(Theme.P(12), Theme.P(7), Theme.P(12), Theme.P(7)), Margin = new Padding(0, 0, Theme.P(8), 0) };
            var caption = Theme.Label(titles[i], 10, Theme.Muted); caption.Dock = DockStyle.Top; caption.Height = Theme.P(20);
            figures[i] = Theme.Label("—", 25, i == 0 ? Theme.Blue : Theme.Ink, true); figures[i].Font = Theme.Font(25, true, true); figures[i].Dock = DockStyle.Bottom; figures[i].Height = Theme.P(36);
            panel.Controls.Add(figures[i]); panel.Controls.Add(caption); summary.Controls.Add(panel, i, 0);
        }
        root.Controls.Add(summary, 0, 1);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, Theme.P(12), 0) };
        var dayHeader = new Panel { Dock = DockStyle.Top, Height = Theme.P(43) };
        var dayTitle = Theme.Label("История по дням", 15, bold: true); dayTitle.Dock = DockStyle.Left; dayTitle.Width = Theme.P(160); dayHeader.Controls.Add(dayTitle);
        emptyDays.Dock = DockStyle.Right; dayHeader.Controls.Add(emptyDays); left.Controls.Add(days); left.Controls.Add(dayHeader);
        var right = new Panel { Dock = DockStyle.Fill };
        var detailHeader = new Panel { Dock = DockStyle.Top, Height = Theme.P(62) };
        detailTitle.Dock = DockStyle.Top; detailTitle.Height = Theme.P(29); detailNote.Dock = DockStyle.Bottom; detailNote.Height = Theme.P(29);
        detailHeader.Controls.AddRange([detailTitle, detailNote]); right.Controls.Add(keys); right.Controls.Add(detailHeader);
        body.Controls.Add(left, 0, 0); body.Controls.Add(right, 1, 0); root.Controls.Add(body, 0, 2);
        var footer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, Theme.P(6), 0, 0) };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, Width = Theme.P(455), WrapContents = false };
        var wholePeriod = new ModernButton { Text = "Все дни", Width = Theme.P(104) };
        buttons.Controls.AddRange([exportPeriod, exportDay, wholePeriod]); footer.Controls.Add(buttons); status.Dock = DockStyle.Fill; footer.Controls.Add(status); status.BringToFront(); root.Controls.Add(footer, 0, 3);
        application.SelectedIndexChanged += (_, _) => { if (!binding) Rebuild(); };
        group.SelectedIndexChanged += (_, _) => { if (!binding) Rebuild(); };
        search.TextChanged += (_, _) => { if (!binding) Rebuild(); };
        emptyDays.CheckedChanged += (_, _) => { if (!binding) Rebuild(); };
        reset.Click += (_, _) => { binding = true; application.SelectedIndex = 0; group.SelectedIndex = 0; search.Clear(); emptyDays.Checked = false; binding = false; Rebuild(); };
        days.SelectionChanged += (_, _) =>
        {
            if (binding) return;
            var column = days.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.DataPropertyName == "date");
            if (column != null && days.CurrentRow?.Selected == true && days.CurrentRow.Cells[column.Index].Value is DateTime date) { selectedDay = DateOnly.FromDateTime(date); Details(); }
        };
        days.CellPainting += PaintDayBar;
        wholePeriod.Click += (_, _) => { selectedDay = null; days.ClearSelection(); Details(); };
        exportPeriod.Click += (_, _) => Export(false); exportDay.Click += (_, _) => Export(true);
        exportPeriod.Enabled = exportDay.Enabled = false;
    }
    private static ComboBox Choice() => new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = Theme.Font() };
    private static Panel FilterBox(string title, Control input)
    {
        var box = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, Theme.P(10), Theme.P(12)), Margin = Padding.Empty };
        var label = Theme.Label(title, 10, Theme.Muted); label.Dock = DockStyle.Top; label.Height = Theme.P(22);
        var host = new Panel { Dock = DockStyle.Bottom, Height = Theme.P(33) }; host.Controls.Add(input); box.Controls.Add(host); box.Controls.Add(label); return box;
    }
    private static DataGridView Table() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Theme.Card, BorderStyle = BorderStyle.None,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, Font = Theme.Font(12), RowTemplate = { Height = Theme.P(39) },
        ColumnHeadersHeight = Theme.P(34), ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        EnableHeadersVisualStyles = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = Theme.Line,
        DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Theme.Ink, BackColor = Theme.Card, SelectionBackColor = Theme.BlueSoft, SelectionForeColor = Theme.Ink, Padding = new Padding(Theme.P(6), 0, 0, 0) },
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { ForeColor = Theme.Muted, BackColor = Theme.Page, Font = Theme.Font(10, true) }
    };
    public void SetSource(IReadOnlyList<KeyMetricRow> rows, DateOnly begin, DateOnly end,IReadOnlyDictionary<string,ApplicationIdentity>? names=null)
    {
        if (valid && from == begin && to == end && source.SequenceEqual(rows)) return;
        source = rows.ToArray(); from = begin; to = end; valid = from <= to;if(names!=null)appNames=names;
        var selected = (application.SelectedItem as AppChoice)?.Id;
        var choices = rows.Select(r => r.Key.App).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
        if (selected != null && !choices.Contains(selected)) choices.Add(selected);
        binding = true; application.Items.Clear(); application.Items.Add(new AppChoice(null,"Все приложения"));
        application.Items.AddRange(choices.Select(id=>(object)new AppChoice(id,appNames.TryGetValue(id,out var info)?info.Caption:ApplicationIdentity.Unknown(id).Caption)).ToArray());
        application.SelectedIndex=selected==null?0:application.Items.Cast<AppChoice>().ToList().FindIndex(a=>a.Id==selected);binding=false;Rebuild();
    }
    public void InvalidPeriod()
    { valid = false; report = null; status.Text = "Проверьте даты периода"; exportPeriod.Enabled = exportDay.Enabled = false; days.DataSource = keys.DataSource = null; }
    private void Rebuild()
    {
        if (!valid) return;
        var filter = new StatisticsFilter(from, to, (application.SelectedItem as AppChoice)?.Id, (KeyGroup)Math.Max(0, group.SelectedIndex), search.Text, emptyDays.Checked);
        report = KeyStatistics.Build(source, filter);
        figures[0].Text = report.Presses.ToString("N0"); figures[1].Text = report.DistinctKeys.ToString("N0"); figures[2].Text = report.DataDays.ToString("N0"); figures[3].Text = report.Applications.ToString("N0");
        var table = new DataTable(); table.Columns.Add("date", typeof(DateTime)); table.Columns.Add("presses", typeof(long)); table.Columns.Add("unique", typeof(int));
        foreach (var day in report.Days) table.Rows.Add(day.Date.ToDateTime(TimeOnly.MinValue), day.Presses, day.DistinctKeys);
        var oldDay = selectedDay; binding = true; days.DataSource = table.DefaultView;
        foreach (DataGridViewColumn col in days.Columns)
        { col.HeaderText = col.DataPropertyName switch { "date" => "День", "presses" => "Нажатий", _ => "Клавиш" }; col.SortMode = DataGridViewColumnSortMode.Automatic; if (col.DataPropertyName == "date") col.DefaultCellStyle.Format = "dd.MM.yyyy"; }
        if (oldDay != null && !report.Days.Any(d => d.Date == oldDay)) selectedDay = null;
        if (oldDay != null && selectedDay != null)
        {
            foreach (DataGridViewRow row in days.Rows) if (row.Cells[0].Value is DateTime d && DateOnly.FromDateTime(d) == selectedDay) { days.CurrentCell = row.Cells[0]; row.Selected = true; break; }
        }
        else days.ClearSelection();
        binding = false; exportPeriod.Enabled = true;
        status.Text = $"{from:dd.MM.yyyy} — {to:dd.MM.yyyy} · {report.DataDays:N0} дней с записями";
        Details();
    }
    private void Details()
    {
        if (report == null) return;
        var detail = report.Breakdown(selectedDay); var table = new DataTable();
        table.Columns.Add("key", typeof(string)); table.Columns.Add("presses", typeof(long)); table.Columns.Add("share", typeof(double)); table.Columns.Add("repeats", typeof(long)); table.Columns.Add("apps", typeof(string));
        foreach (var k in detail)
        {
            var labels=report.Rows.Where(r=>r.Code==k.Code&&(selectedDay==null||r.Key.LocalDate==selectedDay.Value.ToString("yyyy-MM-dd"))).Select(r=>r.Key.App).Distinct()
                .Select(id=>appNames.TryGetValue(id,out var info)?info.Caption:ApplicationIdentity.Unknown(id).Caption);
            table.Rows.Add(k.Label,k.Presses,k.Share,k.Repeats,string.Join(", ",labels));
        }
        keys.DataSource = table.DefaultView;
        foreach (DataGridViewColumn col in keys.Columns)
        {
            col.HeaderText = col.DataPropertyName switch { "key" => "Клавиша", "presses" => "Нажатий", "share" => "Доля, %", "repeats" => "Повторы", _ => "Приложения" };
            col.SortMode = DataGridViewColumnSortMode.Automatic;
            if (col.DataPropertyName == "key") col.FillWeight = 140;if(col.DataPropertyName=="apps")col.FillWeight=240;
        }
        detailTitle.Text = selectedDay?.ToString("d MMMM yyyy, ddd", Ru) ?? "Клавиши за весь период";
        detailNote.Text = detail.Count == 0 ? "Нет записей для текущих фильтров" : $"{detail.Sum(k => k.Presses):N0} нажатий · {detail.Count:N0} клавиш · чаще всего: {detail[0].Label}";
        exportDay.Enabled = selectedDay != null;
    }
    private void PaintDayBar(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || report == null || days.Columns[e.ColumnIndex].DataPropertyName != "presses") return;
        e.PaintBackground(e.ClipBounds, true); var max = Math.Max(1, report.Days.Select(d => d.Presses).DefaultIfEmpty(0).Max());
        var value = Convert.ToInt64(e.Value); var width = (int)((e.CellBounds.Width - Theme.P(12)) * value / (double)max);
        if (width > 0) { using var brush = new SolidBrush(Color.FromArgb(216, 231, 252)); e.Graphics!.FillRectangle(brush, e.CellBounds.X + Theme.P(4), e.CellBounds.Y + Theme.P(8), width, e.CellBounds.Height - Theme.P(16)); }
        e.PaintContent(e.ClipBounds); e.Handled = true;
    }
    public void ExportPeriod() => Export(false);
    private void Export(bool onlyDay)
    {
        if (report == null || (onlyDay && selectedDay == null)) return;
        var snapshot = report; var date = onlyDay ? selectedDay : null;
        using var options = new ExportStatisticsDialog(snapshot, date);
        if (options.ShowDialog(FindForm()) != DialogResult.OK) return;
        using var file = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "typingstats-statistics-" + (date?.ToString("yyyyMMdd") ?? from.ToString("yyyyMMdd") + "-" + to.ToString("yyyyMMdd")) + ".csv" };
        if (file.ShowDialog(FindForm()) != DialogResult.OK) return;
        try { StatisticsCsv.Write(snapshot, file.FileName, options.Format, date,appNames); MessageBox.Show(FindForm(), "CSV сохранён. Выгрузка учитывает выбранные фильтры.", "TypingStats"); }
        catch (Exception e) { MessageBox.Show(FindForm(), "Не удалось сохранить статистику: " + e.Message, "TypingStats", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
    public void VerifyExport(string folder)
    {
        if (report == null) return;
        StatisticsCsv.Write(report, Path.Combine(folder, "statistics-period.csv"), StatisticsExport.Detailed);
        StatisticsCsv.Write(report, Path.Combine(folder, "statistics-days.csv"), StatisticsExport.Daily);
    }
    public void VerifyView()
    {
        if(report==null)return;
        var dataDay=report.Days.FirstOrDefault(d=>d.Presses>0);selectedDay=dataDay?.Date;Details();
        if(dataDay!=null&&report.Breakdown(selectedDay).Sum(k=>k.Presses)!=dataDay.Presses)throw new InvalidOperationException("Daily statistics mismatch");
    }
}

internal sealed class ExportStatisticsDialog : Form
{
    private readonly ComboBox format = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = Theme.Font() };
    public StatisticsExport Format => (StatisticsExport)Math.Max(0, format.SelectedIndex);
    public ExportStatisticsDialog(KeyStatisticsReport report, DateOnly? day)
    {
        Theme.Window(this); Icon = AppIcons.Application; Text = "Выгрузка статистики"; ClientSize = new Size(Theme.P(460), Theme.P(244)); StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        var title = Theme.Label(day == null ? "Экспорт всего периода" : "Экспорт выбранного дня", 19, bold: true); title.SetBounds(Theme.P(22), Theme.P(18), Theme.P(420), Theme.P(33)); Controls.Add(title);
        var description = Theme.Label(day == null ? $"{report.Filter.From:dd.MM.yyyy} — {report.Filter.To:dd.MM.yyyy}" : day.Value.ToString("dd.MM.yyyy"), 12, Theme.Muted); description.SetBounds(Theme.P(22), Theme.P(58), Theme.P(420), Theme.P(25)); Controls.Add(description);
        format.Items.AddRange(["Подробно: день → приложение → клавиша", "Сводка по дням", "Суммы по клавишам"]); format.SelectedIndex = 0; format.SetBounds(Theme.P(22), Theme.P(98), Theme.P(416), Theme.P(34)); Controls.Add(format);
        var note = Theme.Label("Применяются текущие фильтры. UTF-8 CSV для Excel.", 11, Theme.Muted); note.SetBounds(Theme.P(22), Theme.P(139), Theme.P(420), Theme.P(24)); Controls.Add(note);
        var save = new ModernButton { Text = "Продолжить", Primary = true, DialogResult = DialogResult.OK }; save.SetBounds(Theme.P(296), Theme.P(184), Theme.P(142), Theme.P(36)); Controls.Add(save); AcceptButton = save;
        var cancel = new ModernButton { Text = "Отмена", DialogResult = DialogResult.Cancel }; cancel.SetBounds(Theme.P(180), Theme.P(184), Theme.P(104), Theme.P(36)); Controls.Add(cancel); CancelButton = cancel;
    }
}
