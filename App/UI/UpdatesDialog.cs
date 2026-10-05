using TypingStats.App.Updates;
using TypingStats.Core;

namespace TypingStats.App.UI;

internal sealed class UpdatesDialog : Form
{
    private readonly Collector collector;
    private readonly UpdateCoordinator coordinator;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Label message = Theme.Label("", 13, Theme.Muted);
    private readonly ModernButton check = new() { Text = "Проверить", Primary = true, Width = Theme.P(130) };
    private readonly ModernButton install = new() { Text = "Обновить", Width = Theme.P(130), Enabled = false };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100 };
    private readonly CheckBox previews = new() { Text = "Включать предварительные версии", Checked = true, AutoSize = true, Font = Theme.Font() };
    private ReleaseUpdate? available;
    private bool committing;
    private bool cleaned;
    public bool RestartRequested { get; private set; }
    public UpdatesDialog(Collector collector, UpdateCoordinator coordinator)
    {
        this.collector = collector; this.coordinator = coordinator; Theme.Window(this); Icon = AppIcons.Application; Text = "Обновления TypingStats"; ClientSize = new Size(Theme.P(590), Theme.P(366));
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        var heading = Theme.Label("Обновления через GitHub", 22, bold: true); heading.SetBounds(Theme.P(22), Theme.P(18), Theme.P(470), Theme.P(38)); Controls.Add(heading);
        var current = Theme.Label("Установлена версия " + GitHubUpdates.Current.ToString(3) + " · " + UpdatePackage.Repository, 12, Theme.Muted); current.SetBounds(Theme.P(22), Theme.P(64), Theme.P(478), Theme.P(27)); Controls.Add(current);
        message.Text = coordinator.Status; message.SetBounds(Theme.P(22), Theme.P(100), Theme.P(546), Theme.P(65)); Controls.Add(message);
        progress.SetBounds(Theme.P(22), Theme.P(174), Theme.P(546), Theme.P(8)); Controls.Add(progress);
        var automatic = new ToggleSwitch { Text="Автоматически скачивать и устанавливать", Checked=collector.Settings.AutomaticUpdates };
        automatic.SetBounds(Theme.P(22),Theme.P(192),Theme.P(546),Theme.P(42));Controls.Add(automatic);
        automatic.CheckedChanged += (_,_) => { collector.Settings.AutomaticUpdates=automatic.Checked;try {collector.Settings.Save();}catch(Exception e){message.Text=e.Message;} };
        previews.Checked = collector.Settings.IncludePreviewUpdates; previews.Location = new Point(Theme.P(22), Theme.P(240)); Controls.Add(previews);
        previews.CheckedChanged += (_,_)=>{collector.Settings.IncludePreviewUpdates=previews.Checked;try{collector.Settings.Save();}catch(Exception e){message.Text=e.Message;}};
        var hint=Theme.Label("Проверка раз в 6 ч. Установка в трее после минуты без ввода.",11,Theme.Muted);hint.SetBounds(Theme.P(22),Theme.P(277),Theme.P(546),Theme.P(25));Controls.Add(hint);
        check.Location = new Point(Theme.P(296), Theme.P(316)); install.Location = new Point(Theme.P(440), Theme.P(316)); Controls.AddRange([check, install]);
        check.Click += async (_, _) =>
        {
            check.Enabled = install.Enabled = false; message.Text = "Проверяем GitHub Releases…";
            try { available = await coordinator.Check(cancellation.Token); if(!IsDisposed) {message.Text = available == null ? "У вас последняя доступная версия." : "Доступна " + available.Version.ToString(3) + ". История и настройки сохранятся при обновлении."; install.Enabled = available != null;} }
            catch (Exception e) { if (!IsDisposed) message.Text = "Не удалось проверить: " + e.Message; }
            finally { if (!IsDisposed) check.Enabled = true; }
        };
        install.Click += async (_, _) =>
        {
            if (available == null) return;
            if (MessageBox.Show(this, "Обновить TypingStats до " + available.Version.ToString(3) + "? Программа сохранит данные и перезапустится.", "Обновление", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
            install.Enabled = check.Enabled = false; message.Text = "Скачиваем и проверяем SHA-256…";
            try
            {
                // Keep this dialog alive once installation has begun; the shared coordinator
                // serializes manual operations with background checking/downloading.
                committing=true;
                await coordinator.Install(available, new Progress<int>(n => { if (!IsDisposed) progress.Value = Math.Clamp(n, 0, 100); }), cancellation.Token);
                RestartRequested = true; DialogResult = DialogResult.OK; Close();
            }
            catch (Exception e) { committing=false;if (!IsDisposed) { message.Text = "Обновление не установлено: " + e.Message; install.Enabled = true; check.Enabled = true; } }
        };
        FormClosing += (_, e) => { if(committing&&!RestartRequested){e.Cancel=true;return;}cancellation.Cancel(); };
    }
    protected override void Dispose(bool disposing) { if (disposing&&!cleaned) { cleaned=true;cancellation.Cancel(); cancellation.Dispose(); } base.Dispose(disposing); }
}
