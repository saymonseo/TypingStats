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
    private readonly Label state = new() { Dock = DockStyle.Bottom, Height = Theme.P(32), Padding = new Padding(Theme.P(18), 0, 0, 0), ForeColor = Theme.Muted, Font = Theme.Font(11), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label[] values = new Label[4];
    private readonly HistoryChart chart = new() { Dock = DockStyle.Fill };
    private readonly Heatmap heatmap = new() { Dock = DockStyle.Fill };
    private readonly DataGridView apps = Grid(), sessions = Grid(), profiles = Grid();
    private readonly TextBox quality = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = new Font("Segoe UI", 11) };
    private readonly ModernButton pause = new() { Text = "Пауза", Width = Theme.P(120), Primary = true };
    private IReadOnlyList<MetricRow> rows = [];
    private bool updating;
    private TableLayoutPanel? layout;
    private readonly Panel pageHost = new() { Dock = DockStyle.Fill, BackColor = Theme.Card };
    private readonly List<Control> pages = new();
    private readonly List<ModernButton> navigation = new();
    private readonly Label sectionTitle = Theme.Label("Динамика набора", 17, bold: true);
    private readonly Label sectionHint = Theme.Label("Распределение символов за выбранный период", 11, Theme.Muted);
    private int selectedPage;
    private readonly TextBox appSearch = new() { Dock = DockStyle.Top, PlaceholderText = "Найти приложение…", Height = 30 };
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AllowExit { get; set; }
    public event Action? ExitRequested;
    public MainForm(Collector collector)
    {
        this.collector = collector; Icon = AppIcons.Application; Theme.Window(this);
        Text = "TypingStats — статистика набора"; StartPosition = FormStartPosition.CenterScreen;
        var workArea = Screen.PrimaryScreen!.WorkingArea;
        Size = new Size(Math.Min(Theme.P(1180), workArea.Width - Theme.P(36)), Math.Min(Theme.P(810), workArea.Height - Theme.P(36)));
        MinimumSize = new Size(Math.Min(Theme.P(980), workArea.Width - 30), Math.Min(Theme.P(660), workArea.Height - 30));
        var side = new Panel { Dock = DockStyle.Left, Width = Theme.P(190), BackColor = Theme.Ink, Padding = new Padding(Theme.P(12)) };
        var branding = new Panel { Dock = DockStyle.Top, Height = Theme.P(92) };
        var mark = new PictureBox { Image = AppIcons.Application.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(Theme.P(8), Theme.P(10)), Size = new Size(Theme.P(32), Theme.P(32)) };
        var brand = Theme.Label("TypingStats", 19, Color.White, true); brand.Location = new Point(Theme.P(48), Theme.P(5)); brand.Size = new Size(Theme.P(126), Theme.P(38));
        var tagline = Theme.Label("ЛОКАЛЬНАЯ СТАТИСТИКА", 9, Color.FromArgb(145, 165, 191)); tagline.Location = new Point(Theme.P(8), Theme.P(53)); tagline.Size = new Size(Theme.P(166), Theme.P(20));
        branding.Controls.AddRange([mark, brand, tagline]); side.Controls.Add(branding);
        var sideFooter = Theme.Label("0.1.1  /  Windows\nДанные на этом компьютере", 10, Color.FromArgb(156, 175, 199)); sideFooter.Dock = DockStyle.Bottom; sideFooter.Height = Theme.P(52); sideFooter.Padding = new Padding(Theme.P(8), 0, 0, 0); side.Controls.Add(sideFooter);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, Theme.P(8), 0, 0) }; side.Controls.Add(nav); nav.BringToFront();
        var shell = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Page }; Controls.Add(shell); Controls.Add(side);
        shell.Controls.Add(state);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(Theme.P(24), Theme.P(14), Theme.P(24), Theme.P(14)) };
        layout = root;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(76))); root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(48)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(126))); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.Controls.Add(root); root.BringToFront();
        var title = Theme.Label("Ваш ритм набора", 28, Theme.Ink, true); title.Dock = DockStyle.Top; title.Height = Theme.P(44); title.Font = Theme.Font(28, true, true);
        var head = new Panel { Dock = DockStyle.Fill }; head.Controls.Add(title);
        var subtitle = Theme.Label("История, приложения и привычки работы с текстом", 12, Theme.Muted); subtitle.Dock = DockStyle.Bottom; subtitle.Height = Theme.P(26); head.Controls.Add(subtitle); root.Controls.Add(head, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        period.Items.AddRange(["Сегодня", "Вчера", "7 дней", "30 дней", "Всё время", "Период"]); period.SelectedIndex = 0;
        period.Font = Theme.Font(); period.FlatStyle = FlatStyle.Flat; period.Width = Theme.P(155); period.Height = Theme.P(36);
        start.Font = end.Font = Theme.Font(); start.Width = end.Width = Theme.P(140);
        period.Margin = start.Margin = end.Margin = new Padding(0, 0, Theme.P(8), 0);
        actions.Controls.AddRange([period, start, end, pause]);
        AddButton(actions, "Экспорт CSV", Export);
        root.Controls.Add(actions, 0, 1);
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        var captions = new[] { "Напечатано", "После правок ≈", "Активное время", "Символов / мин" };
        for (var i = 0; i < 4; i++)
        {
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var p = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, Theme.P(4), i == 3 ? 0 : Theme.P(10), Theme.P(14)), Padding = new Padding(Theme.P(14)) };
            var label = Theme.Label(captions[i], 11, Theme.Muted); label.Dock = DockStyle.Top; label.Height = Theme.P(23);
            values[i] = Theme.Label("—", 34, i == 0 ? Theme.Blue : Theme.Ink, true); values[i].Dock = DockStyle.Bottom; values[i].Height = Theme.P(50); values[i].Font = Theme.Font(34, true, true);
            var figure = values[i]; figure.Tag = 34; figure.AutoEllipsis = true;
            figure.TextChanged += (_, _) => FitMetric(figure); figure.SizeChanged += (_, _) => FitMetric(figure);
            p.Controls.Add(values[i]); p.Controls.Add(label); cards.Controls.Add(p, i, 0);
        }
        root.Controls.Add(cards, 0, 2);
        var appPanel = new Panel { Dock = DockStyle.Fill }; appPanel.Controls.Add(apps); appPanel.Controls.Add(appSearch);
        appSearch.Font = Theme.Font(); appSearch.BorderStyle = BorderStyle.FixedSingle; appSearch.BackColor = Theme.Page;
        appSearch.TextChanged += (_, _) => RefreshData();
        quality.Font = Theme.Font(12); quality.ForeColor = Theme.Muted; quality.BackColor = Theme.Card;
        pages.AddRange([chart, appPanel, heatmap, sessions, profiles, quality]);
        var titles = new[] { "Обзор", "Приложения", "Тепловая карта", "Сессии", "Раскладки", "Качество данных" };
        for (var i = 0; i < titles.Length; i++)
        {
            var index = i; var button = new ModernButton { Text = titles[i], Navigation = true, Width = Theme.P(158), Height = Theme.P(42), Margin = new Padding(0, 0, 0, Theme.P(5)) };
            button.Click += (_, _) => SelectPage(index); navigation.Add(button); nav.Controls.Add(button);
        }
        var utilities = Theme.Label("УПРАВЛЕНИЕ", 9, Color.FromArgb(126, 150, 180)); utilities.Size = new Size(Theme.P(158), Theme.P(35)); utilities.Padding = new Padding(Theme.P(18), Theme.P(8), 0, 0); nav.Controls.Add(utilities);
        void SideAction(string label, Action action)
        { var button = new ModernButton { Text = label, Navigation = true, Width = Theme.P(158), Height = Theme.P(36), Margin = new Padding(0, 0, 0, Theme.P(4)) }; button.Click += (_, _) => action(); nav.Controls.Add(button); }
        SideAction("Настройки", () => { using var dialog = new SettingsDialog(collector.Settings); dialog.ShowDialog(this); });
        SideAction("Резервная копия", Backup); SideAction("Другие действия", MoreMenu);
        var content = new CardPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var pageHeader = new Panel { Dock = DockStyle.Top, Height = Theme.P(55) };
        sectionTitle.Dock = DockStyle.Top; sectionTitle.Height = Theme.P(27); sectionHint.Dock = DockStyle.Bottom; sectionHint.Height = Theme.P(24); pageHeader.Controls.AddRange([sectionTitle, sectionHint]);
        content.Controls.Add(pageHost); content.Controls.Add(pageHeader); root.Controls.Add(content, 0, 3); SelectPage(0);
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
        BackgroundColor = Theme.Card, BorderStyle = BorderStyle.None, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, ColumnHeadersHeight = Theme.P(38), ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        RowTemplate = { Height = Theme.P(40) }, EnableHeadersVisualStyles = false, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
        GridColor = Theme.Line, Font = Theme.Font(12),
        DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Theme.Ink, BackColor = Theme.Card, SelectionBackColor = Theme.BlueSoft, SelectionForeColor = Theme.Ink, Padding = new Padding(Theme.P(8), 0, 0, 0) },
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Theme.Page, ForeColor = Theme.Muted, Font = Theme.Font(11, true), Padding = new Padding(Theme.P(8), 0, 0, 0) }
    };
    private void SelectPage(int index)
    {
        selectedPage = index; pageHost.Controls.Clear(); pageHost.Controls.Add(pages[index]);
        for (var i = 0; i < navigation.Count; i++) { navigation[i].Selected = i == index; navigation[i].Invalidate(); }
        sectionTitle.Text = new[] { "Динамика набора", "Где вы печатаете", "Ритм по дням и часам", "Сессии работы", "Профили ввода", "Качество измерений" }[index];
        sectionHint.Text = new[] { "Символы за выбранный период", "Двойной щелчок — назначить категорию", "Чем насыщеннее цвет, тем больше набрано", "Завершённые сессии и активное время", "Раскладка не определяет язык текста", "Измеренные результаты, оценки и пропуски" }[index];
    }
    private static void AddButton(FlowLayoutPanel p, string text, Action action)
    { var b = new ModernButton { Text = text, Width = Theme.P(128) }; b.Click += (_, _) => action(); p.Controls.Add(b); }
    private void UpdateDates()
    {
        updating = true; end.Value = DateTime.Today;
        start.Value = period.SelectedIndex switch { 1 => DateTime.Today.AddDays(-1), 2 => DateTime.Today.AddDays(-6), 3 => DateTime.Today.AddDays(-29), 4 => new DateTime(2000, 1, 1), _ => DateTime.Today };
        if (period.SelectedIndex == 1) end.Value = start.Value;
        start.Enabled = end.Enabled = period.SelectedIndex == 5;
        start.Visible = end.Visible = period.SelectedIndex == 5;
        if (layout != null) layout.RowStyles[1].Height = Theme.P(period.SelectedIndex == 5 ? 84 : 48);
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
            var speedSize = c.ActiveMs < 30000 ? 14 : 34;
            if ((int?)values[3].Tag != speedSize) { values[3].Tag = speedSize; FitMetric(values[3]); }
            pause.Text = collector.Paused ? "Продолжить" : "Пауза";
            state.Text = (collector.StorageError ?? collector.Status) + $"   ·   Измерено {c.Observed:N0}   ·   Оценено {c.Estimated:N0}   ·   Пропуски {c.Unresolved + c.Lost:N0}";
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
        foreach (DataGridViewColumn column in grid.Columns) if (captions.TryGetValue(column.DataPropertyName, out var caption)) column.HeaderText = caption;
        if (selected != null) foreach (DataGridViewRow row in grid.Rows)
            if (row.Cells[0].Value?.ToString() == selected) { row.Selected = true; break; }
    }
    private static void FitMetric(Label label)
    {
        if (label.Width <= 0) return;
        var size = label.Tag is int max ? max : 34;
        Font candidate;
        while (true)
        {
            candidate = Theme.Font(size, true, true);
            if (TextRenderer.MeasureText(label.Text, candidate, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width <= label.Width - 2 || size <= 12) break;
            candidate.Dispose(); size -= 2;
        }
        if (Math.Abs(label.Font.Size - candidate.Size) < .1f) candidate.Dispose();
        else { var previous = label.Font; label.Font = candidate; previous.Dispose(); }
    }
    public void RenderVerificationTabs(string folder)
    {
        var selected = selectedPage;
        for (var i = 0; i < pages.Count; i++)
        {
            SelectPage(i); Refresh(); using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, "tab-" + i + ".png"));
        }
        SelectPage(selected);
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
        Theme.Menu(menu);
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
        using var form = new Form { Text = "Категория приложения", Icon = AppIcons.Application, ClientSize = new Size(Theme.P(390), Theme.P(180)), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        Theme.Window(form);
        var title = Theme.Label(app, 17, bold: true); title.Location = new Point(Theme.P(20), Theme.P(16)); title.Size = new Size(Theme.P(350), Theme.P(34)); form.Controls.Add(title);
        var box = new ComboBox { Left = Theme.P(20), Top = Theme.P(66), Width = Theme.P(350), DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Flat, Font = Theme.Font() };
        box.Items.AddRange(["Работа", "Общение", "Код", "Прочее"]); box.Text = collector.Store.Categories().GetValueOrDefault(app, "Прочее"); form.Controls.Add(box);
        var ok = new ModernButton { Text = "Сохранить", Left = Theme.P(230), Top = Theme.P(122), Width = Theme.P(140), Primary = true, DialogResult = DialogResult.OK }; form.Controls.Add(ok); form.AcceptButton = ok;
        var cancel = new ModernButton { Text = "Отмена", Left = Theme.P(120), Top = Theme.P(122), Width = Theme.P(100), DialogResult = DialogResult.Cancel }; form.Controls.Add(cancel); form.CancelButton = cancel;
        if (form.ShowDialog(this) == DialogResult.OK) try { collector.Store.SetCategory(app, box.Text.Trim()); RefreshData(); } catch (Exception e) { ShowError(e); }
    }
    private void ShowError(Exception e) => MessageBox.Show(this, e.Message, "TypingStats", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}
