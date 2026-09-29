namespace TypingStats.App.UI;

internal sealed class SettingsDialog : Form
{
    private readonly Settings settings;
    public SettingsDialog(Settings settings)
    {
        this.settings = settings; Text = "Настройки TypingStats"; ClientSize = new Size(590, 490);
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; Font = new Font("Segoe UI", 10);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 9 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30)); Controls.Add(panel);
        NumericUpDown AddNumber(string label, int value, int min, int max, int row)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, row);
            var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 100 }; panel.Controls.Add(n, 1, row); return n;
        }
        var idle = AddNumber("Интервал активности, секунд", settings.IdleSeconds, 1, 30, 0);
        var gap = AddNumber("Перерыв между сессиями, секунд", settings.SessionSeconds, 30, 600, 1);
        var retention = AddNumber("Хранить минутные данные, дней", settings.MinuteDays, 1, 36500, 2);
        var goal = AddNumber("Дневная цель (0 — выключена)", settings.DailyGoal, 0, 1000000, 3);
        var auto = new CheckBox { Text = "Запускать вместе с Windows", Checked = Settings.AutoStartEnabled(), AutoSize = true };
        panel.Controls.Add(auto, 0, 4); panel.SetColumnSpan(auto, 2);
        var minimized = new CheckBox { Text = "Открывать сразу в трее", Checked = settings.StartMinimized, AutoSize = true };
        panel.Controls.Add(minimized, 0, 5); panel.SetColumnSpan(minimized, 2);
        var composition = new CheckBox { Text = "Результаты IME через UI Automation", Checked = settings.EnableCompositionResults, AutoSize = true };
        panel.Controls.Add(composition, 0, 6); panel.SetColumnSpan(composition, 2);
        panel.Controls.Add(new Label { Text = "Пауза: Ctrl+Alt + выбранная клавиша", AutoSize = true }, 0, 7);
        var key = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        key.Items.AddRange(Enumerable.Range(1, 12).Select(i => (object)("F" + i)).ToArray()); key.SelectedIndex = settings.HotkeyVk - 0x70; panel.Controls.Add(key, 1, 7);
        var save = new Button { Text = "Сохранить", AutoSize = true, Anchor = AnchorStyles.Right };
        panel.Controls.Add(save, 1, 8); AcceptButton = save;
        save.Click += (_, _) =>
        {
            try
            {
                Settings.AutoStart(auto.Checked);
                settings.IdleSeconds = (int)idle.Value; settings.SessionSeconds = (int)gap.Value;
                settings.MinuteDays = (int)retention.Value; settings.DailyGoal = (int)goal.Value;
                settings.StartMinimized = minimized.Checked; settings.EnableCompositionResults = composition.Checked;
                settings.HotkeyVk = 0x70 + key.SelectedIndex;
                settings.Save(); DialogResult = DialogResult.OK; Close();
            }
            catch (Exception e) { MessageBox.Show(this, "Настройки не сохранены: " + e.Message, "TypingStats"); }
        };
    }
}
