using System.Diagnostics;
using System.Globalization;
using System.Data;
using TypingStats.Core;
using TypingStats.Storage;
using TypingStats.App.Windows;

namespace TypingStats.App.UI;

internal sealed class MainForm : Form
{
    private readonly Collector collector;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly ComboBox period = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 125 };
    private readonly DateTimePicker start = new() { Format = DateTimePickerFormat.Short, Width = 145 };
    private readonly DateTimePicker end = new() { Format = DateTimePickerFormat.Short, Width = 145 };
    private readonly Label state = new() { Dock = DockStyle.Bottom, Height = 28, Padding = new Padding(14, 4, 0, 0) };
    private readonly Label[] values = new Label[4];
    private readonly HistoryChart chart = new() { Dock = DockStyle.Fill };
    private readonly Heatmap heatmap = new() { Dock = DockStyle.Fill };
    private readonly DataGridView apps = Grid(), sessions = Grid(), profiles = Grid();
    private readonly TextBox quality = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = new Font("Segoe UI", 11) };
    private readonly Button pause = new() { Text = "Пауза", AutoSize = true };
    private IReadOnlyList<MetricRow> rows = [];
    private bool updating;
    private TableLayoutPanel? layout;
    private TabControl? tabControl;
    private readonly TextBox appSearch = new() { Dock = DockStyle.Top, PlaceholderText = "Найти приложение…", Height = 30 };
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AllowExit { get; set; }
    public event Action? ExitRequested;
    public MainForm(Collector collector)
    {
        this.collector = collector;
        Text = "TypingStats — статистика набора"; Size = new Size(1120, 780); MinimumSize = new Size(850, 620);
        Font = new Font("Segoe UI", 9); BackColor = Color.FromArgb(244, 247, 250); StartPosition = FormStartPosition.CenterScreen;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(18) };
        layout = root;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Controls.Add(root); Controls.Add(state);
        var title = new Label { Text = "TypingStats", Font = new Font("Segoe UI", 25, FontStyle.Bold), ForeColor = Color.FromArgb(30, 55, 75), Dock = DockStyle.Top, Height = 65 };
        var head = new Panel { Dock = DockStyle.Fill }; head.Controls.Add(title);
        var subtitle = new Label { Text = "Ваш набор. Ваш ритм. Локальная статистика.", Dock = DockStyle.Bottom, Height = 24, ForeColor = Color.FromArgb(95, 110, 125) }; head.Controls.Add(subtitle); root.Controls.Add(head, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        period.Items.AddRange(["Сегодня", "Вчера", "7 дней", "30 дней", "Всё время", "Период"]); period.SelectedIndex = 0;
        actions.Controls.AddRange([period, start, end, pause]);
        AddButton(actions, "Экспорт CSV", Export);
        AddButton(actions, "Настройки", () => { using var dialog = new SettingsDialog(collector.Settings); dialog.ShowDialog(this); });
        AddButton(actions, "Копия базы", Backup);
        AddButton(actions, "Ещё ▾", MoreMenu);
        root.Controls.Add(actions, 0, 1);
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        var captions = new[] { "НАПЕЧАТАНО", "ПОСЛЕ ПРАВОК ≈", "АКТИВНОЕ ВРЕМЯ", "СИМВОЛОВ / МИН" };
        for (var i = 0; i < 4; i++)
        {
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 4, 10, 12), Padding = new Padding(14) };
            var label = new Label { Text = captions[i], Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 8.5f), ForeColor = Color.FromArgb(95, 110, 125) };
            values[i] = new Label { Text = "—", Dock = DockStyle.Bottom, Height = 70, Font = new Font("Segoe UI", 22, FontStyle.Bold), ForeColor = Color.FromArgb(35, 70, 95) };
            p.Controls.Add(values[i]); p.Controls.Add(label); cards.Controls.Add(p, i, 0);
        }
        root.Controls.Add(cards, 0, 2);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabControl = tabs;
        var appPanel = new Panel { Dock = DockStyle.Fill }; appPanel.Controls.Add(apps); appPanel.Controls.Add(appSearch);
        appSearch.TextChanged += (_, _) => RefreshData();
        AddTab(tabs, "Обзор", chart); AddTab(tabs, "Приложения", appPanel); AddTab(tabs, "Тепловая карта", heatmap);
        AddTab(tabs, "Сессии", sessions); AddTab(tabs, "Раскладки", profiles); AddTab(tabs, "Качество", quality); root.Controls.Add(tabs, 0, 3);
        var tooltip = new ToolTip(); tooltip.SetToolTip(values[1], "Оценка с вычетом связанных Backspace. Не равна длине документа. За выбранный период может быть отрицательной.");
        period.SelectedIndexChanged += (_, _) => { UpdateDates(); RefreshData(); };
        start.ValueChanged += (_, _) => { if (!updating) RefreshData(); }; end.ValueChanged += (_, _) => { if (!updating) RefreshData(); };
        pause.Click += async (_, _) => { try { await collector.TogglePause(); RefreshData(); } catch (Exception e) { ShowError(e); } };
        apps.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) SetCategory(apps.Rows[e.RowIndex].Cells[0].Value?.ToString() ?? ""); };
        FormClosing += (_, e) => { if (!AllowExit) { e.Cancel = true; Hide(); } };
        timer.Tick += (_, _) => { if (Visible) RefreshData(); }; timer.Start(); UpdateDates();
    }
    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.None, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, ColumnHeadersHeight = 35, RowTemplate = { Height = 32 }
    };
    private static void AddTab(TabControl tabs, string title, Control content)
    { var tab = new TabPage(title) { Padding = new Padding(12), BackColor = Color.White }; tab.Controls.Add(content); tabs.TabPages.Add(tab); }
    private static void AddButton(FlowLayoutPanel p, string text, Action action)
    { var b = new Button { Text = text, AutoSize = true }; b.Click += (_, _) => action(); p.Controls.Add(b); }
    private void UpdateDates()
    {
        updating = true; end.Value = DateTime.Today;
        start.Value = period.SelectedIndex switch { 1 => DateTime.Today.AddDays(-1), 2 => DateTime.Today.AddDays(-6), 3 => DateTime.Today.AddDays(-29), 4 => new DateTime(2000, 1, 1), _ => DateTime.Today };
        if (period.SelectedIndex == 1) end.Value = start.Value;
        start.Enabled = end.Enabled = period.SelectedIndex == 5;
        start.Visible = end.Visible = period.SelectedIndex == 5;
        if (layout != null) layout.RowStyles[1].Height = period.SelectedIndex == 5 ? 90 : 49;
        updating = false;
    }
    public void RefreshData()
    {
        if (updating) return;
        try
        {
            if (end.Value.Date < start.Value.Date) { state.Text = "Дата окончания раньше начала"; return; }
            var old = start.Value.Date < DateTime.Today.AddYears(-2);
            rows = collector.Read(start.Value.ToString("yyyy-MM-dd"), end.Value.ToString("yyyy-MM-dd"), old ? 2 : 1);
            var c = Counters.Sum(rows.Select(r => r.Counts));
            values[0].Text = c.Gross.ToString("N0"); values[1].Text = c.Net.ToString("N0");
            values[2].Text = c.ActiveMs >= 3600000 ? $"{c.ActiveMs / 3600000}ч {c.ActiveMs / 60000 % 60}м" : $"{c.ActiveMs / 60000}м {c.ActiveMs / 1000 % 60}с";
            values[3].Text = c.ActiveMs < 30000 ? "мало данных" : (c.Gross * 60000.0 / c.ActiveMs).ToString("N0");
            var speedSize = c.ActiveMs < 30000 ? 12 : 22;
            if (Math.Abs(values[3].Font.SizeInPoints - speedSize) > .1f)
            { var previous = values[3].Font; values[3].Font = new Font("Segoe UI", speedSize, FontStyle.Bold); previous.Dispose(); }
            pause.Text = collector.Paused ? "Продолжить" : "Пауза";
            state.Text = (collector.StorageError ?? collector.Status) + $"   •   По результату: {c.Observed:N0}; оценено: {c.Estimated:N0}; неизвестных: {c.Unresolved:N0}; потерь: {c.Lost:N0}";
            var single = start.Value.Date == end.Value.Date;
            chart.Values = single && !old
                ? Enumerable.Range(0, 24).Select(h => { var n = Counters.Sum(rows.Where(r => r.Key.Hour == h).Select(r => r.Counts)); return (h.ToString("00"), n.Gross, n.Net); }).ToArray()
                : rows.GroupBy(r => r.Key.LocalDate).OrderBy(g => g.Key).Select(g => { var n = Counters.Sum(g.Select(r => r.Counts)); return (DateTime.Parse(g.Key).ToString("dd.MM"), n.Gross, n.Net); }).ToArray();
            chart.Invalidate(); heatmap.Rows = old ? [] : rows; heatmap.Invalidate();
            var cats = collector.Store.Categories();
            Bind(apps, rows.Where(r => r.Key.App.Contains(appSearch.Text, StringComparison.CurrentCultureIgnoreCase)).GroupBy(r => r.Key.App).Select(g =>
            {
                var n = Counters.Sum(g.Select(r => r.Counts));
                return new { Приложение = g.Key, Категория = cats.GetValueOrDefault(g.Key, "Прочее"), Напечатано = n.Gross,
                    ПослеИсправлений = n.Net, Backspace = n.Backspaces, Delete = n.Deletes, Минуты = Math.Round(n.ActiveMs / 60000.0, 1),
                    ОцененоПроцентов = n.Gross == 0 ? 0 : Math.Round(n.Estimated * 100.0 / n.Gross, 1) };
            }).OrderByDescending(x => x.Напечатано).ToArray());
            Bind(profiles, rows.GroupBy(r => r.Key.Profile).Select(g => { var n = Counters.Sum(g.Select(r => r.Counts)); return new { ПрофильВвода = g.Key, Напечатано = n.Gross, ПоРезультату = n.Observed, Оценено = n.Estimated, Неизвестных = n.Unresolved }; }).OrderByDescending(x => x.Напечатано).ToArray());
            Bind(sessions, collector.Store.Sessions().Where(s => string.CompareOrdinal(s.Start.ToLocalTime().ToString("yyyy-MM-dd"), start.Value.ToString("yyyy-MM-dd")) >= 0 && string.CompareOrdinal(s.Start.ToLocalTime().ToString("yyyy-MM-dd"), end.Value.ToString("yyyy-MM-dd")) <= 0)
                .Select(s => new { Начало = s.Start.ToLocalTime().ToString("dd.MM HH:mm"), Окончание = s.End.ToLocalTime().ToString("HH:mm"), Напечатано = s.Gross, АктивныхМинут = Math.Round(s.ActiveMs / 60000.0, 1), Причина = s.Reason }).ToArray());
            quality.Text = $"Состояние: {collector.Status}\r\n{collector.UiaStatus}\r\n\r\nПо результату композиции: {c.Observed:N0}\r\nОценено по клавишам: {c.Estimated:N0}\r\nНеизвестных сценариев: {c.Unresolved:N0}\r\nПотерянных событий: {c.Lost:N0}\r\nInjected без установленного источника: {c.Injected:N0}\r\n\r\nBackspace: {c.Backspaces:N0}; связанных: {c.LinkedBackspaces:N0}\r\nDelete: {c.Deletes:N0}; удалений слов: {c.WordDeletes:N0}\r\nВставок клавишами: {c.Pastes:N0}; Undo: {c.Undo:N0}; Redo: {c.Redo:N0}\r\n\r\nВсе клавиатурные значения — оценки, включая команды нестандартных редакторов.\r\nEnter/Tab не прибавляются без проверенного контекста. Dead keys отмечаются как неизвестные.\r\nРезультаты IME учитываются при полученных событиях Composition + Finalized.\r\nEmoji-панель, software keyboard и нестандартные IME ещё не заявлены как проверенные.\r\n\r\nПосле исправлений — оценка с вычетом связанных Backspace, не длина документа.\r\nОтрицательное значение за период означает удаление ввода из прошлого периода.\r\nПолная совместимость конкретных программ требует проверки.\r\n\r\nДанные: {collector.Settings.DataDirectory}\r\nХранится только статистика. Текст, заголовки окон и clipboard не записываются.\r\n{(old ? "Для старого периода доступны дневные данные; почасовая карта недоступна." : "")}";
        }
        catch (Exception e) { state.Text = "Не удалось прочитать данные: " + e.GetType().Name; }
    }
    private static void Bind<T>(DataGridView grid, T[] data)
    {
        var selected = grid.CurrentRow?.Cells[0].Value?.ToString();
        var sort = (grid.DataSource as DataView)?.Sort ?? "";
        var fields = typeof(T).GetProperties(); var table = new DataTable();
        foreach (var field in fields) table.Columns.Add(field.Name, field.PropertyType);
        foreach (var item in data) table.Rows.Add(fields.Select(f => f.GetValue(item)).ToArray());
        var view = table.DefaultView; if (!string.IsNullOrEmpty(sort)) view.Sort = sort;
        grid.DataSource = view;
        foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.Automatic;
        var captions = new Dictionary<string, string> { ["ПослеИсправлений"] = "Правки ≈", ["ОцененоПроцентов"] = "Оценено, %", ["АктивныхМинут"] = "Активно, мин", ["Минуты"] = "Активно, мин", ["ПрофильВвода"] = "Профиль ввода", ["ПоРезультату"] = "По результату" };
        foreach (DataGridViewColumn column in grid.Columns) if (captions.TryGetValue(column.Name, out var caption)) column.HeaderText = caption;
        if (selected != null) foreach (DataGridViewRow row in grid.Rows)
            if (row.Cells[0].Value?.ToString() == selected) { row.Selected = true; break; }
    }
    public void RenderVerificationTabs(string folder)
    {
        if (tabControl == null) return;
        var selected = tabControl.SelectedIndex;
        for (var i = 0; i < tabControl.TabCount; i++)
        {
            tabControl.SelectedIndex = i; Refresh(); using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, "tab-" + i + ".png"));
        }
        tabControl.SelectedIndex = selected;
        using var dialog = new SettingsDialog(collector.Settings); dialog.Show(this); dialog.PerformLayout(); dialog.Refresh();
        using var settingsBitmap = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(settingsBitmap, new Rectangle(Point.Empty, settingsBitmap.Size)); settingsBitmap.Save(Path.Combine(folder, "settings-ui.png"));
        dialog.Close();
    }
    private void Export()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "typing-stats-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { StatsStore.Export(rows, dialog.FileName); MessageBox.Show(this, "Статистика экспортирована.", "TypingStats"); } catch (Exception e) { ShowError(e); }
    }
    private async void Backup()
    {
        using var dialog = new SaveFileDialog { Filter = "SQLite (*.sqlite)|*.sqlite", FileName = "typing-stats-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".sqlite" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { await collector.ChangeData(s => s.Backup(dialog.FileName)); MessageBox.Show(this, "Копия создана.", "TypingStats"); } catch (Exception e) { ShowError(e); }
    }
    private void MoreMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Сохранить сейчас", null, async (_, _) => { try { await collector.SaveNow(); } catch (Exception e) { ShowError(e); } });
        menu.Items.Add("Восстановить из копии…", null, (_, _) => Restore());
        menu.Items.Add("Удалить всю статистику…", null, (_, _) => Clear());
        menu.Items.Add("Открыть папку данных", null, (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", "\"" + collector.Settings.DataDirectory + "\"") { UseShellExecute = true }));
        menu.Items.Add("Выход", null, (_, _) => ExitRequested?.Invoke()); menu.Show(Cursor.Position);
    }
    private async void Restore()
    {
        using var dialog = new OpenFileDialog { Filter = "SQLite (*.sqlite)|*.sqlite" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this, "Заменить текущую статистику выбранной копией? Перед заменой будет сохранена текущая база.", "Восстановление", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            await collector.ChangeData(s => { s.Backup(Path.Combine(collector.Settings.DataDirectory, "before-restore-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".sqlite")); s.Restore(dialog.FileName); }); RefreshData();
        }
        catch (Exception e) { ShowError(e); }
    }
    private async void Clear()
    {
        if (MessageBox.Show(this, "Удалить ВСЮ статистику? Перед удалением будет создана резервная копия.", "Удаление", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        try
        {
            await collector.ChangeData(s => { s.Backup(Path.Combine(collector.Settings.DataDirectory, "before-clear-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".sqlite")); s.Clear(); }); RefreshData();
        }
        catch (Exception e) { ShowError(e); }
    }
    private void SetCategory(string app)
    {
        using var form = new Form { Text = "Категория: " + app, ClientSize = new Size(320, 110), StartPosition = FormStartPosition.CenterParent, Font = Font };
        var box = new ComboBox { Left = 15, Top = 15, Width = 280, DropDownStyle = ComboBoxStyle.DropDown };
        box.Items.AddRange(["Работа", "Общение", "Код", "Прочее"]); box.Text = collector.Store.Categories().GetValueOrDefault(app, "Прочее"); form.Controls.Add(box);
        var ok = new Button { Text = "Сохранить", Left = 190, Top = 55, Width = 105, DialogResult = DialogResult.OK }; form.Controls.Add(ok); form.AcceptButton = ok;
        if (form.ShowDialog(this) == DialogResult.OK) try { collector.Store.SetCategory(app, box.Text.Trim()); RefreshData(); } catch (Exception e) { ShowError(e); }
    }
    private void ShowError(Exception e) => MessageBox.Show(this, e.Message, "TypingStats", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}
