using System.Diagnostics;
using System.Globalization;
using System.Data;
using TypingStats.Core;
using TypingStats.Storage;
using TypingStats.App.Windows;
using TypingStats.App.Updates;

namespace TypingStats.App.UI;

internal sealed class MainForm : Form
{
    private readonly Collector collector;
    private readonly UpdateCoordinator updates;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly ComboBox period = new ThemedComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 125 };
    private readonly DateTimePicker start = new() { Format = DateTimePickerFormat.Short, Width = 145 };
    private readonly DateTimePicker end = new() { Format = DateTimePickerFormat.Short, Width = 145 };
    private readonly Label state = new() { Dock = DockStyle.Bottom, Height = Theme.P(32), Padding = new Padding(Theme.P(18), 0, 0, 0), ForeColor = Theme.Muted, Font = Theme.Font(11), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label[] values = new Label[4];
    private readonly Label[] metricCaptions = new Label[4];
    private readonly ComboBox modePicker = new ThemedComboBox() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Width = Theme.P(172), Font = Theme.Font() };
    private readonly KeyboardMap keyboardMap = new() { Dock = DockStyle.Top, Height = Theme.P(232) };
    private readonly DataGridView keyGrid = Grid();
    private readonly StatisticsPage statistics = new();
    private readonly MouseStatisticsPage mouseStatistics = new();
    private TableLayoutPanel? cardsHost;
    private IReadOnlyList<KeyMetricRow> keyRows = [];
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
    private static readonly HashSet<Label> fittingMetrics = new();
    private readonly Label sectionTitle = Theme.Label("Динамика набора", 17, bold: true);
    private readonly Label sectionHint = Theme.Label("Распределение символов за выбранный период", 11, Theme.Muted);
    private int selectedPage;
    private readonly TextBox appSearch = new() { Dock = DockStyle.Top, PlaceholderText = "Найти приложение…", Height = 30 };
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AllowExit { get; set; }
    public event Action? ExitRequested;
    public MainForm(Collector collector, UpdateCoordinator updates)
    {
        this.collector = collector; this.updates = updates; Icon = AppIcons.Application; Theme.Window(this);
        Text = "TypingStats — статистика набора"; StartPosition = FormStartPosition.CenterScreen;
        var workArea = Screen.PrimaryScreen!.WorkingArea;
        Size = new Size(Math.Min(Theme.P(1180), workArea.Width - Theme.P(36)), Math.Min(Theme.P(810), workArea.Height - Theme.P(36)));
        MinimumSize = new Size(Math.Min(Theme.P(980), workArea.Width - 30), Math.Min(Theme.P(660), workArea.Height - 30));
        var side = new Panel { Name = "Sidebar", Dock = DockStyle.Left, Width = Theme.P(190), BackColor = Theme.Sidebar, Padding = new Padding(Theme.P(12)) };
        var branding = new Panel { Dock = DockStyle.Top, Height = Theme.P(92) };
        var mark = new PictureBox { Image = AppIcons.Application.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(Theme.P(8), Theme.P(10)), Size = new Size(Theme.P(32), Theme.P(32)) };
        var brand = Theme.Label("TypingStats", 19, Color.White, true); brand.Location = new Point(Theme.P(48), Theme.P(5)); brand.Size = new Size(Theme.P(126), Theme.P(38));
        var tagline = Theme.Label("ЛОКАЛЬНАЯ СТАТИСТИКА", 9, Color.FromArgb(145, 165, 191)); tagline.Location = new Point(Theme.P(8), Theme.P(53)); tagline.Size = new Size(Theme.P(166), Theme.P(20));
        branding.Controls.AddRange([mark, brand, tagline]); side.Controls.Add(branding);
        var sideFooter = Theme.Label("0.5.0  /  Windows\nДанные на этом компьютере", 10, Color.FromArgb(156, 175, 199)); sideFooter.Dock = DockStyle.Bottom; sideFooter.Height = Theme.P(52); sideFooter.Padding = new Padding(Theme.P(8), 0, 0, 0); side.Controls.Add(sideFooter);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, Theme.P(8), 0, 0) }; side.Controls.Add(nav); nav.BringToFront();
        var shell = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Page }; Controls.Add(shell); Controls.Add(side);
        shell.Controls.Add(state);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(Theme.P(24), Theme.P(14), Theme.P(24), Theme.P(14)) };
        layout = root;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(76))); root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(48)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(126))); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.Controls.Add(root); root.BringToFront();
        var title = Theme.Label("Ваш ритм набора", 28, Theme.Ink, true); title.Dock = DockStyle.Top; title.Height = Theme.P(44); title.Font = Theme.Font(28, true, true);
        var head = new Panel { Dock = DockStyle.Fill }; head.Controls.Add(title);
        var subtitle = Theme.Label("Клавиатура, мышь и привычки работы за компьютером", 12, Theme.Muted); subtitle.Dock = DockStyle.Bottom; subtitle.Height = Theme.P(26); head.Controls.Add(subtitle); root.Controls.Add(head, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
        period.Items.AddRange(["Сегодня", "Вчера", "7 дней", "30 дней", "Всё время", "Период"]); period.SelectedIndex = 0;
        period.Font = Theme.Font(); period.FlatStyle = FlatStyle.Flat; period.Width = Theme.P(155); period.Height = Theme.P(36);
        start.Font = end.Font = Theme.Font(); start.Width = end.Width = Theme.P(140);
        period.Margin = start.Margin = end.Margin = new Padding(0, 0, Theme.P(8), 0);
        modePicker.Items.AddRange(["Текст", "Все нажатия", "Текст + клавиши"]); modePicker.SelectedIndex = (int)collector.Settings.Mode;
        modePicker.Margin = new Padding(0, 0, Theme.P(8), 0);
        actions.Controls.AddRange([modePicker, period, start, end, pause]);
        AddButton(actions, "Экспорт CSV", Export);
        root.Controls.Add(actions, 0, 1);
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        cardsHost = cards;
        var captions = new[] { "Напечатано", "После правок ≈", "Активное время", "Символов / мин" };
        for (var i = 0; i < 4; i++)
        {
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var p = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, Theme.P(4), i == 3 ? 0 : Theme.P(10), Theme.P(14)), Padding = new Padding(Theme.P(14)) };
            var label = Theme.Label(captions[i], 11, Theme.Muted); label.Dock = DockStyle.Top; label.Height = Theme.P(23);
            metricCaptions[i] = label;
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
        var keyboardPage = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card }; keyboardPage.Controls.Add(keyGrid); keyboardPage.Controls.Add(keyboardMap);
        pages.AddRange([chart, statistics, appPanel, keyboardPage, heatmap, sessions, profiles, quality, mouseStatistics]);
        var titles = new[] { "Обзор", "Статистика", "Приложения", "Клавиши", "Тепловая карта", "Сессии", "Раскладки", "Качество данных", "Мышь" };
        for (var i = 0; i < titles.Length; i++)
        {
            var index = i; var button = new ModernButton { Text = titles[i], Navigation = true, Width = Theme.P(158), Height = Theme.P(42), Margin = new Padding(0, 0, 0, Theme.P(5)) };
            button.Click += (_, _) => SelectPage(index); navigation.Add(button); nav.Controls.Add(button);
        }
        nav.Controls.SetChildIndex(navigation[8], 4); // Mouse sits beside keyboard statistics.
        var utilities = Theme.Label("УПРАВЛЕНИЕ", 9, Color.FromArgb(126, 150, 180)); utilities.Size = new Size(Theme.P(158), Theme.P(35)); utilities.Padding = new Padding(Theme.P(18), Theme.P(8), 0, 0); nav.Controls.Add(utilities);
        void SideAction(string label, Action action)
        { var button = new ModernButton { Text = label, Navigation = true, Width = Theme.P(158), Height = Theme.P(36), Margin = new Padding(0, 0, 0, Theme.P(4)) }; button.Click += (_, _) => action(); nav.Controls.Add(button); }
        SideAction("Настройки", () => { using var dialog = new SettingsDialog(collector.Settings); dialog.ShowDialog(this); });
        SideAction("Тема оформления", ThemeMenu);
        SideAction("Резервная копия", Backup); SideAction("Обновления", Updates); SideAction("Другие действия", MoreMenu);
        var content = new CardPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var pageHeader = new Panel { Dock = DockStyle.Top, Height = Theme.P(55) };
        sectionTitle.Dock = DockStyle.Top; sectionTitle.Height = Theme.P(27); sectionHint.Dock = DockStyle.Bottom; sectionHint.Height = Theme.P(24); pageHeader.Controls.AddRange([sectionTitle, sectionHint]);
        content.Controls.Add(pageHost); content.Controls.Add(pageHeader); root.Controls.Add(content, 0, 3); SelectPage(0);
        var tooltip = new ToolTip(); tooltip.SetToolTip(values[1], "Оценка с вычетом связанных Backspace. Не равна длине документа. За выбранный период может быть отрицательной.");
        period.SelectedIndexChanged += (_, _) => { UpdateDates(); RefreshData(); };
        modePicker.SelectedIndexChanged += async (_, _) =>
        {
            if (updating || modePicker.SelectedIndex < 0 || (int)collector.Settings.Mode == modePicker.SelectedIndex) return;
            try { await collector.SetMode((TrackingMode)modePicker.SelectedIndex); RefreshData(); } catch (Exception e) { ShowError(e); }
        };
        start.ValueChanged += (_, _) => { if (!updating) RefreshData(); }; end.ValueChanged += (_, _) => { if (!updating) RefreshData(); };
        pause.Click += async (_, _) => { try { await collector.TogglePause(); RefreshData(); } catch (Exception e) { ShowError(e); } };
        apps.CellDoubleClick += (_, e) =>
        { var code=apps.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c=>c.DataPropertyName=="Код");if(e.RowIndex>=0&&code!=null)SetCategory(apps.Rows[e.RowIndex].Cells[code.Index].Value?.ToString()??""); };
        FormClosing += (_, e) => { if (!AllowExit) { e.Cancel = true; Hide(); } };
        timer.Tick += (_, _) => { if (Visible) RefreshData(); }; timer.Start(); UpdateDates();
        Theme.Capture(this); foreach(var page in pages) Theme.Capture(page);
        Theme.Changed += ApplyTheme; ApplyTheme();
    }
    private void ApplyTheme() { if (IsDisposed) return; Theme.Apply(this); foreach(var page in pages) Theme.Apply(page); foreach(var grid in new[]{apps,sessions,profiles,keyGrid})Theme.Table(grid); }
    private void ThemeMenu()
    {
        var menu = new ContextMenuStrip(); Theme.Menu(menu);
        foreach (var (preference, caption) in new[] { (AppTheme.Light, "Светлая"), (AppTheme.Dark, "Тёмная"), (AppTheme.System, "Как в Windows") })
        {
            var item = new ToolStripMenuItem(caption) { Checked = collector.Settings.Theme == preference };
            item.Click += (_, _) => { try { collector.Settings.Theme = preference; collector.Settings.Save(); Theme.Set(preference); } catch(Exception e) { ShowError(e); } }; menu.Items.Add(item);
        }
        menu.Closed += (_, _) => menu.Dispose(); menu.Show(Cursor.Position);
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
        if (cardsHost != null) cardsHost.Visible = index is not (1 or 8);
        if (layout != null) layout.RowStyles[2].Height = index is 1 or 8 ? 0 : Theme.P(126);
        for (var i = 0; i < navigation.Count; i++) { navigation[i].Selected = i == index; navigation[i].Invalidate(); }
        sectionTitle.Text = new[] { "Динамика активности", "Подробная статистика", "Статистика по приложениям", "Какие клавиши вы нажимаете", "Ритм по дням и часам", "Сессии работы", "Профили ввода", "Качество измерений", "Отдельная статистика мыши" }[index];
        sectionHint.Text = new[] { "Показатели выбранного режима", "Выберите период сверху, затем день слева — справа его клавиши", "Двойной щелчок — назначить категорию", "Основной блок на схеме; Numpad и другие кнопки — в таблице", "Чем насыщеннее цвет, тем больше активность", "Завершённые сессии и активное время", "Раскладка не определяет язык текста", "Измеренные результаты, оценки и пропуски", "Независимо от режима клавиатуры · ЛКМ, ПКМ, средняя, X1 и X2" }[index];
        RefreshData();
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
            if (end.Value.Date < start.Value.Date) { state.Text = "Дата окончания раньше начала"; statistics.InvalidPeriod(); mouseStatistics.InvalidPeriod(); return; }
            var old = start.Value.Date < DateTime.Today.AddYears(-2);
            rows = collector.Read(start.Value.ToString("yyyy-MM-dd"), end.Value.ToString("yyyy-MM-dd"), old ? 2 : 1);
            keyRows = collector.ReadKeys(start.Value.ToString("yyyy-MM-dd"), end.Value.ToString("yyyy-MM-dd"));
            var appNames=collector.Store.ApplicationNames();
            if (selectedPage == 1) statistics.SetSource(keyRows, DateOnly.FromDateTime(start.Value), DateOnly.FromDateTime(end.Value),appNames);
            if (selectedPage == 8) mouseStatistics.SetSource(collector.ReadMouse(start.Value.ToString("yyyy-MM-dd"), end.Value.ToString("yyyy-MM-dd")), DateOnly.FromDateTime(start.Value), DateOnly.FromDateTime(end.Value), appNames, collector.Settings.TrackMouse);
            var keysMode = collector.Settings.Mode == TrackingMode.Keys;
            updating = true; modePicker.SelectedIndex = (int)collector.Settings.Mode; updating = false;
            var c = Counters.Sum(rows.Select(r => r.Counts));
            var captions = keysMode ? new[] { "Всего нажатий", "Клавиш использовано", "Активное время", "Нажатий / мин" } : new[] { "Напечатано", "После правок ≈", "Активное время", "Символов / мин" };
            for (var i = 0; i < 4; i++) metricCaptions[i].Text = captions[i];
            values[0].Text = (keysMode ? c.KeyPresses : c.Gross).ToString("N0"); values[1].Text = keysMode ? keyRows.Select(k => k.Code).Distinct().Count().ToString("N0") : c.Net.ToString("N0");
            var active = keysMode ? c.KeyActiveMs : c.ActiveMs;
            values[2].Text = active >= 3600000 ? $"{active / 3600000}ч {active / 60000 % 60}м" : $"{active / 60000}м {active / 1000 % 60}с";
            values[3].Text = active < 30000 ? "мало данных" : ((keysMode ? c.KeyPresses : c.Gross) * 60000.0 / active).ToString("N0");
            var speedSize = active < 30000 ? 14 : 34;
            if ((int?)values[3].Tag != speedSize) { values[3].Tag = speedSize; FitMetric(values[3]); }
            pause.Text = collector.Paused ? "Продолжить" : "Пауза";
            state.Text = (collector.StorageError ?? collector.Status) + $"   ·   Нажатий {c.KeyPresses:N0}   ·   Измерено {c.Observed:N0}   ·   Оценено {c.Estimated:N0}   ·   Пропуски {c.Unresolved + c.Lost:N0}";
            var single = start.Value.Date == end.Value.Date;
            chart.Values = single && !old
                ? Enumerable.Range(0, 24).Select(h => { var n = Counters.Sum(rows.Where(r => r.Key.Hour == h).Select(r => r.Counts)); return (h.ToString("00"), keysMode ? n.KeyPresses : n.Gross, keysMode ? 0 : n.Net); }).ToArray()
                : rows.GroupBy(r => r.Key.LocalDate).OrderBy(g => g.Key).Select(g => { var n = Counters.Sum(g.Select(r => r.Counts)); return (DateTime.Parse(g.Key).ToString("dd.MM"), keysMode ? n.KeyPresses : n.Gross, keysMode ? 0 : n.Net); }).ToArray();
            chart.KeysMode = keysMode; chart.Invalidate(); heatmap.KeysMode = keysMode; heatmap.Rows = old ? [] : rows; heatmap.Invalidate();
            keyboardMap.SetRows(keyRows);
            var keyTotal = Math.Max(1, keyRows.Sum(k => k.Presses));
            Bind(keyGrid, keyRows.GroupBy(k => k.Code).Select(g => new { Клавиша = g.First().Label, Нажатий = g.Sum(k => k.Presses), ДоляПроцентов = Math.Round(g.Sum(k => k.Presses) * 100.0 / keyTotal, 2), Автоповтор = g.Sum(k => k.Repeats), Программные = g.Sum(k => k.Injected) }).OrderByDescending(k => k.Нажатий).ToArray());
            var cats = collector.Store.Categories();
            Bind(apps, rows.Where(r => (appNames.TryGetValue(r.Key.App,out var info)?info.Caption:r.Key.App).Contains(appSearch.Text, StringComparison.CurrentCultureIgnoreCase)).GroupBy(r => r.Key.App).Select(g =>
            {
                var n = Counters.Sum(g.Select(r => r.Counts));
                return new { Приложение = appNames.TryGetValue(g.Key,out var info)?info.Caption:g.Key, Код=g.Key, Категория = cats.GetValueOrDefault(g.Key, "Прочее"), Нажатий = n.KeyPresses, Напечатано = n.Gross,
                    ПослеИсправлений = n.Net, Backspace = n.Backspaces, Delete = n.Deletes, Минуты = Math.Round(n.ActiveMs / 60000.0, 1),
                    ОцененоПроцентов = n.Gross == 0 ? 0 : Math.Round(n.Estimated * 100.0 / n.Gross, 1) };
            }).OrderByDescending(x => keysMode ? x.Нажатий : x.Напечатано).ToArray());
            foreach(DataGridViewColumn col in apps.Columns)if(col.DataPropertyName=="Код")col.Visible=false;
            Bind(profiles, rows.GroupBy(r => r.Key.Profile).Select(g => { var n = Counters.Sum(g.Select(r => r.Counts)); return new { ПрофильВвода = g.Key, Нажатий = n.KeyPresses, Напечатано = n.Gross, ПоРезультату = n.Observed, Оценено = n.Estimated, Неизвестных = n.Unresolved }; }).OrderByDescending(x => keysMode ? x.Нажатий : x.Напечатано).ToArray());
            Bind(sessions, collector.Store.Sessions().Where(s => string.CompareOrdinal(s.Start.ToLocalTime().ToString("yyyy-MM-dd"), start.Value.ToString("yyyy-MM-dd")) >= 0 && string.CompareOrdinal(s.Start.ToLocalTime().ToString("yyyy-MM-dd"), end.Value.ToString("yyyy-MM-dd")) <= 0)
                .Select(s => new { Начало = s.Start.ToLocalTime().ToString("dd.MM HH:mm"), Окончание = s.End.ToLocalTime().ToString("HH:mm"), Нажатий = s.KeyPresses, Напечатано = s.Gross, АктивныхМинут = Math.Round((keysMode ? s.KeyActiveMs : s.ActiveMs) / 60000.0, 1), Причина = s.Reason }).ToArray());
            quality.Text = $"Состояние: {collector.Status}\r\n{collector.UiaStatus}\r\n\r\nПо результату композиции: {c.Observed:N0}\r\nОценено по клавишам: {c.Estimated:N0}\r\nНеизвестных сценариев: {c.Unresolved:N0}\r\nПотерянных событий: {c.Lost:N0}\r\nInjected без установленного источника: {c.Injected:N0}\r\n\r\nBackspace: {c.Backspaces:N0}; связанных: {c.LinkedBackspaces:N0}\r\nDelete: {c.Deletes:N0}; удалений слов: {c.WordDeletes:N0}\r\nВставок клавишами: {c.Pastes:N0}; Undo: {c.Undo:N0}; Redo: {c.Redo:N0}\r\n\r\nВсе клавиатурные значения — оценки, включая команды нестандартных редакторов.\r\nEnter/Tab не прибавляются без проверенного контекста. Dead keys отмечаются как неизвестные.\r\nРезультаты IME учитываются при полученных событиях Composition + Finalized.\r\nEmoji-панель, software keyboard и нестандартные IME ещё не заявлены как проверенные.\r\n\r\nПосле исправлений — оценка с вычетом связанных Backspace, не длина документа.\r\nОтрицательное значение за период означает удаление ввода из прошлого периода.\r\nПолная совместимость конкретных программ требует проверки.\r\n\r\nДанные: {collector.Settings.DataDirectory}\r\nХранится только статистика. Текст, заголовки окон и clipboard не записываются.\r\n{(old ? "Для старого периода доступны дневные данные; почасовая карта недоступна." : "")}";
        }
        catch (Exception e) { updating = false; state.Text = "Не удалось прочитать данные: " + e.GetType().Name; }
    }
    private static void Bind<T>(DataGridView grid, T[] data)
    {
        var selected = grid.CurrentRow?.Cells[0].Value?.ToString();
        var sort = (grid.DataSource as DataView)?.Sort ?? "";
        var fields = typeof(T).GetProperties(); var table = new DataTable();
        foreach (var field in fields) table.Columns.Add(field.Name, field.PropertyType);
        foreach (var item in data) table.Rows.Add(fields.Select(f => f.GetValue(item)).ToArray());
        var view = table.DefaultView; if (!string.IsNullOrEmpty(sort)) try { view.Sort = sort; } catch (Exception e) when (e is DataException or IndexOutOfRangeException or ArgumentException) { }
        grid.DataSource = view;
        Theme.Table(grid);
        foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.Automatic;
        var captions = new Dictionary<string, string> { ["ДоляПроцентов"] = "Доля, %", ["ПослеИсправлений"] = "Правки ≈", ["ОцененоПроцентов"] = "Оценено, %", ["АктивныхМинут"] = "Активно, мин", ["Минуты"] = "Активно, мин", ["ПрофильВвода"] = "Профиль ввода", ["ПоРезультату"] = "По результату" };
        foreach (DataGridViewColumn column in grid.Columns) if (captions.TryGetValue(column.DataPropertyName, out var caption)) column.HeaderText = caption;
        if (selected != null) foreach (DataGridViewRow row in grid.Rows)
            if (row.Cells[0].Value?.ToString() == selected) { row.Selected = true; break; }
    }
    private static void FitMetric(Label label)
    {
        if (label.Parent == null || !fittingMetrics.Add(label)) return;
        try
        {
        var availableWidth = label.Parent.ClientSize.Width - label.Parent.Padding.Horizontal;
        if (availableWidth <= 0) return;
        label.Width = availableWidth;
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
        // Label.OnFontChanged can restore its default requested width (100 px).
        label.Width = availableWidth;
        }
        finally { fittingMetrics.Remove(label); }
    }
    public void RenderVerificationTabs(string folder)
    {
        File.WriteAllText(Path.Combine(folder, "metric-layout.json"), System.Text.Json.JsonSerializer.Serialize(values.Select(v => new { v.Text, Width = v.Width, Height = v.Height, FontSize = v.Font.Size, Unit = v.Font.Unit.ToString(), ParentWidth = v.Parent?.Width }).ToArray()));
        var selected = selectedPage;
        for (var i = 0; i < pages.Count; i++)
        {
            SelectPage(i);if(i==1)statistics.VerifyView(); if(i==8)mouseStatistics.VerifyExport(folder); Refresh(); using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, "tab-" + i + ".png"));
        }
        SelectPage(selected);
        statistics.VerifyExport(folder);
        using var dialog = new SettingsDialog(collector.Settings); dialog.Show(this); dialog.PerformLayout(); dialog.Refresh();
        using var settingsBitmap = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(settingsBitmap, new Rectangle(Point.Empty, settingsBitmap.Size)); settingsBitmap.Save(Path.Combine(folder, "settings-ui.png"));
        dialog.Close();
        using var updateDialog=new UpdatesDialog(collector,updates);updateDialog.Show(this);updateDialog.Refresh();
        using var updateBitmap=new Bitmap(updateDialog.Width,updateDialog.Height);updateDialog.DrawToBitmap(updateBitmap,new Rectangle(Point.Empty,updateBitmap.Size));updateBitmap.Save(Path.Combine(folder,"updates-ui.png"));updateDialog.Close();
        var original = Theme.Preference;
        foreach(var preference in new[] { AppTheme.Dark, AppTheme.Light })
        {
            Theme.Set(preference); SelectPage(8); Refresh(); using var screenshot = new Bitmap(Width, Height); DrawToBitmap(screenshot, new Rectangle(Point.Empty,screenshot.Size));
            screenshot.Save(Path.Combine(folder, preference == AppTheme.Dark ? "mouse-dark.png" : "mouse-light.png"));
            foreach(var page in pages) if(page.BackColor.ToArgb()!=Theme.Card.ToArgb())throw new InvalidOperationException("Detached page theme mismatch");
            if(apps.DefaultCellStyle.ForeColor.ToArgb()!=Theme.Ink.ToArgb() || keyGrid.BackgroundColor.ToArgb()!=Theme.Card.ToArgb())throw new InvalidOperationException($"Grid theme mismatch: apps {apps.DefaultCellStyle.ForeColor}, keys {keyGrid.BackgroundColor}, expected {Theme.Ink} / {Theme.Card}");
        }
        Theme.Set(original); SelectPage(selected);
    }
    private void Export()
    {
        if (selectedPage == 1) { statistics.ExportPeriod(); return; }
        if (selectedPage == 8) { mouseStatistics.ExportPeriod(); return; }
        using var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "typing-stats-" + DateTime.Today.ToString("yyyy-MM-dd") + ".csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { if (selectedPage == 3) StatsStore.ExportKeys(keyRows, dialog.FileName); else StatsStore.Export(rows, dialog.FileName); MessageBox.Show(this, "Статистика экспортирована.", "TypingStats"); } catch (Exception e) { ShowError(e); }
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
        menu.Items.Add("Проверить обновления…", null, (_, _) => Updates());
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
    private void Updates()
    {
        using var dialog = new UpdatesDialog(collector,updates); dialog.ShowDialog(this);
        if (dialog.RestartRequested) ExitRequested?.Invoke();
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
        var box = new ThemedComboBox { Left = Theme.P(20), Top = Theme.P(66), Width = Theme.P(350), DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Flat, Font = Theme.Font() };
        box.Items.AddRange(["Работа", "Общение", "Код", "Прочее"]); box.Text = collector.Store.Categories().GetValueOrDefault(app, "Прочее"); form.Controls.Add(box);
        var ok = new ModernButton { Text = "Сохранить", Left = Theme.P(230), Top = Theme.P(122), Width = Theme.P(140), Primary = true, DialogResult = DialogResult.OK }; form.Controls.Add(ok); form.AcceptButton = ok;
        var cancel = new ModernButton { Text = "Отмена", Left = Theme.P(120), Top = Theme.P(122), Width = Theme.P(100), DialogResult = DialogResult.Cancel }; form.Controls.Add(cancel); form.CancelButton = cancel;
        if (form.ShowDialog(this) == DialogResult.OK) try { collector.Store.SetCategory(app, box.Text.Trim()); RefreshData(); } catch (Exception e) { ShowError(e); }
    }
    private void ShowError(Exception e) => MessageBox.Show(this, e.Message, "TypingStats", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    protected override void Dispose(bool disposing) { if (disposing) { Theme.Changed -= ApplyTheme; timer.Dispose(); } base.Dispose(disposing); }
}
