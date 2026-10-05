using TypingStats.Core;

namespace TypingStats.App.UI;

internal sealed class SettingsDialog : Form
{
    private readonly Settings settings;
    public SettingsDialog(Settings settings)
    {
        this.settings = settings; Icon = AppIcons.Application; Text = "Настройки TypingStats"; Theme.Window(this);
        ClientSize = new Size(Theme.P(680), Math.Min(Theme.P(710), Screen.PrimaryScreen!.WorkingArea.Height - Theme.P(90)));
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(Theme.P(22)), RowCount = 3, ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(76))); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(58))); Controls.Add(root);
        var header = new Panel { Dock = DockStyle.Fill };
        var heading = Theme.Label("Настройте свой ритм", 24, bold: true); heading.Dock = DockStyle.Top; heading.Height = Theme.P(40); heading.Font = Theme.Font(24, true, true);
        var intro = Theme.Label("Запуск, учёт активности и хранение истории", 12, Theme.Muted); intro.Dock = DockStyle.Bottom; intro.Height = Theme.P(26); header.Controls.AddRange([heading, intro]); root.Controls.Add(header, 0, 0);
        var card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, Theme.P(10)) }; root.Controls.Add(card, 0, 1);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, RowCount = 16, BackColor = Theme.Card };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        for (var i = 0; i < 16; i++) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.P(46))); card.Controls.Add(panel);
        var appearanceLabel = Theme.Label("Тема оформления"); appearanceLabel.Dock = DockStyle.Fill; panel.Controls.Add(appearanceLabel,0,0);
        var appearance = new ThemedComboBox { DropDownStyle=ComboBoxStyle.DropDownList, FlatStyle=FlatStyle.Flat, Font=Theme.Font(), Width=Theme.P(168), Anchor=AnchorStyles.Right };
        appearance.Items.AddRange(["Как в Windows", "Светлая", "Тёмная"]); appearance.SelectedIndex=(int)settings.Theme; panel.Controls.Add(appearance,1,0);
        var originalTheme = settings.Theme;
        appearance.SelectedIndexChanged += (_,_) => Theme.Set((AppTheme)appearance.SelectedIndex);
        FormClosed += (_,_) => { if(DialogResult!=DialogResult.OK)Theme.Set(originalTheme); };
        var modeLabel = Theme.Label("Режим учёта"); modeLabel.Dock = DockStyle.Fill; panel.Controls.Add(modeLabel, 0, 1);
        var mode = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = Theme.Font(), Width = Theme.P(168), Anchor = AnchorStyles.Right };
        mode.Items.AddRange(["Текст", "Все нажатия", "Текст + клавиши"]); mode.SelectedIndex = (int)settings.Mode; panel.Controls.Add(mode, 1, 1);
        NumericUpDown AddNumber(string text, int value, int min, int max, int row)
        {
            var label = Theme.Label(text); label.Dock = DockStyle.Fill; panel.Controls.Add(label, 0, row);
            var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = Theme.P(104), Font = Theme.Font(), BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Page, ForeColor = Theme.Ink, Anchor = AnchorStyles.Right };
            panel.Controls.Add(n, 1, row); return n;
        }
        var idle = AddNumber("Интервал активности, секунд", settings.IdleSeconds, 1, 30, 4);
        var gap = AddNumber("Перерыв между сессиями, секунд", settings.SessionSeconds, 30, 600, 5);
        var retention = AddNumber("Минутная история, дней", settings.MinuteDays, 1, 36500, 6);
        var goal = AddNumber("Дневная цель (0 — выключена)", settings.DailyGoal, 0, 1000000, 7);
        ToggleSwitch AddToggle(string label, bool value, int row)
        { var toggle = new ToggleSwitch { Text = label, Checked = value, Dock = DockStyle.Fill }; panel.Controls.Add(toggle, 0, row); panel.SetColumnSpan(toggle, 2); return toggle; }
        var auto = AddToggle("Запускать вместе с Windows", Settings.AutoStartEnabled(), 8);
        var minimized = AddToggle("Открывать сразу в трее", settings.StartMinimized, 9);
        var composition = AddToggle("Результаты IME через UI Automation", settings.EnableCompositionResults, 10);
        var repeat = AddToggle("Автоповтор считать отдельными нажатиями", settings.CountRepeats, 11);
        var injected = AddToggle("Учитывать программные нажатия клавиатуры", settings.CountInjected, 12);
        var mouse = AddToggle("Учитывать кнопки мыши во всех режимах", settings.TrackMouse, 2);
        var mouseInjected = AddToggle("Учитывать программные клики мыши", settings.CountMouseInjected, 3);
        var automatic = AddToggle("Автоматически скачивать и устанавливать обновления", settings.AutomaticUpdates,14);
        var previews = AddToggle("Получать предварительные версии", settings.IncludePreviewUpdates,15);
        var shortcut = Theme.Label("Пауза: Ctrl + Alt + выбранная клавиша"); shortcut.Dock = DockStyle.Fill; panel.Controls.Add(shortcut, 0, 13);
        var key = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Width = Theme.P(104), Font = Theme.Font(), Anchor = AnchorStyles.Right };
        key.Items.AddRange(Enumerable.Range(1, 12).Select(i => (object)("F" + i)).ToArray()); key.SelectedIndex = settings.HotkeyVk - 0x70; panel.Controls.Add(key, 1, 13);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, Theme.P(10), 0, 0) };
        var save = new ModernButton { Text = "Сохранить", Primary = true, Width = Theme.P(142) };
        var cancel = new ModernButton { Text = "Отмена", DialogResult = DialogResult.Cancel, Width = Theme.P(100) };
        footer.Controls.AddRange([save, cancel]); root.Controls.Add(footer, 0, 2); AcceptButton = save; CancelButton = cancel;
        save.Click += (_, _) =>
        {
            try
            {
                Settings.AutoStart(auto.Checked);
                settings.IdleSeconds = (int)idle.Value; settings.SessionSeconds = (int)gap.Value;
                settings.MinuteDays = (int)retention.Value; settings.DailyGoal = (int)goal.Value;
                settings.StartMinimized = minimized.Checked; settings.EnableCompositionResults = composition.Checked;
                settings.HotkeyVk = 0x70 + key.SelectedIndex;
                settings.Mode = (TrackingMode)mode.SelectedIndex; settings.CountRepeats = repeat.Checked; settings.CountInjected = injected.Checked;
                settings.TrackMouse = mouse.Checked; settings.CountMouseInjected = mouseInjected.Checked;
                settings.Theme = (AppTheme)appearance.SelectedIndex; settings.AutomaticUpdates = automatic.Checked; settings.IncludePreviewUpdates = previews.Checked;
                settings.Save(); DialogResult = DialogResult.OK; Close();
            }
            catch (Exception e) { MessageBox.Show(this, "Настройки не сохранены: " + e.Message, "TypingStats"); }
        };
    }
}
