using TypingStats.App.Updates;
using TypingStats.Core;

namespace TypingStats.App.UI;

internal sealed class UpdatesDialog : Form
{
    private readonly Collector collector;
    private readonly GitHubUpdates client = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Label message = Theme.Label("Проверка запускается только по вашей команде.", 13, Theme.Muted);
    private readonly ModernButton check = new() { Text = "Проверить", Primary = true, Width = Theme.P(130) };
    private readonly ModernButton install = new() { Text = "Обновить", Width = Theme.P(130), Enabled = false };
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100 };
    private readonly CheckBox previews = new() { Text = "Включать предварительные версии", Checked = true, AutoSize = true, Font = Theme.Font() };
    private ReleaseUpdate? available;
    private bool committing;
    private bool cleaned;
    public bool RestartRequested { get; private set; }
    public UpdatesDialog(Collector collector)
    {
        this.collector = collector; Theme.Window(this); Icon = AppIcons.Application; Text = "Обновления TypingStats"; ClientSize = new Size(Theme.P(520), Theme.P(280));
        StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        var heading = Theme.Label("Обновления через GitHub", 22, bold: true); heading.SetBounds(Theme.P(22), Theme.P(18), Theme.P(470), Theme.P(38)); Controls.Add(heading);
        var current = Theme.Label("Установлена версия " + GitHubUpdates.Current.ToString(3) + " · " + UpdatePackage.Repository, 12, Theme.Muted); current.SetBounds(Theme.P(22), Theme.P(64), Theme.P(478), Theme.P(27)); Controls.Add(current);
        message.SetBounds(Theme.P(22), Theme.P(100), Theme.P(476), Theme.P(65)); Controls.Add(message);
        progress.SetBounds(Theme.P(22), Theme.P(174), Theme.P(476), Theme.P(8)); Controls.Add(progress);
        previews.Location = new Point(Theme.P(22), Theme.P(195)); Controls.Add(previews);
        check.Location = new Point(Theme.P(224), Theme.P(236)); install.Location = new Point(Theme.P(368), Theme.P(236)); Controls.AddRange([check, install]);
        check.Click += async (_, _) =>
        {
            check.Enabled = install.Enabled = false; message.Text = "Проверяем GitHub Releases…";
            try { available = await client.Check(previews.Checked, cancellation.Token); message.Text = available == null ? "У вас последняя доступная версия." : "Доступна " + available.Version.ToString(3) + ". История и настройки сохранятся при обновлении."; install.Enabled = available != null; }
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
                var staged = await client.Download(available, new Progress<int>(n => { if (!IsDisposed) progress.Value = Math.Clamp(n, 0, 100); }), cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();committing=true;
                message.Text = "Создаём копию базы и готовим перезапуск…"; await GitHubUpdates.Launch(collector, staged);
                RestartRequested = true; DialogResult = DialogResult.OK; Close();
            }
            catch (Exception e) { committing=false;if (!IsDisposed) { message.Text = "Обновление не установлено: " + e.Message; install.Enabled = true; check.Enabled = true; } }
        };
        FormClosing += (_, e) => { if(committing&&!RestartRequested){e.Cancel=true;return;}cancellation.Cancel(); };
    }
    protected override void Dispose(bool disposing) { if (disposing&&!cleaned) { cleaned=true;cancellation.Cancel(); client.Dispose(); cancellation.Dispose(); } base.Dispose(disposing); }
}
