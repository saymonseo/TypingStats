using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using TypingStats.App.UI;
using TypingStats.App.Windows;
using TypingStats.Storage;
using TypingStats.App.Updates;
using TypingStats.Core;

namespace TypingStats.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--test-host")) { Application.Run(new TestHost()); return; }
        var dataIndex = Array.IndexOf(args, "--data-dir");
        var data = dataIndex >= 0 && dataIndex + 1 < args.Length ? Path.GetFullPath(args[dataIndex + 1]) :
            File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag")) ? Path.Combine(AppContext.BaseDirectory, "data") :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TypingStats");
        var smoke = args.Contains("--smoke") || args.Contains("--verify") || args.Contains("--benchmark");
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.ToLowerInvariant())))[..16];
        using var mutex = new Mutex(true, "Local\\TypingStats-" + (smoke ? suffix : Environment.UserName), out var first);
        if (!first)
        {
            var hwnd = FindWindow(null, "TypingStats — статистика набора");
            if (hwnd != 0) Native.PostMessage(hwnd, 0x8001, 0, 0); return;
        }
        try
        {
            var settings = Settings.Load(data); var store = new StatsStore(Path.Combine(data, "stats.sqlite"));
            Theme.Set(settings.Theme);
            using var collector = new Collector(settings, store);
            if(args.Contains("--paused"))collector.SetPaused(true).GetAwaiter().GetResult();
            if (args.Contains("--verify")) collector.VerifyPipeline().GetAwaiter().GetResult();
            if (args.Contains("--verify")) UpdateVerification.Run(collector).GetAwaiter().GetResult();
            using var host = new TrayHost(collector, args.Contains("--minimized") || settings.StartMinimized, smoke, args.Contains("--benchmark"));
            var readyIndex = Array.IndexOf(args, "--update-ready");
            if (readyIndex >= 0 && readyIndex + 1 < args.Length && args[readyIndex + 1].StartsWith("Local\\TypingStatsUpdateReady-", StringComparison.Ordinal))
                try { using var ready = EventWaitHandle.OpenExisting(args[readyIndex + 1]); ready.Set(); } catch (WaitHandleCannotBeOpenedException) { }
            Application.Run(host);
        }
        catch (Exception e)
        {
            if (smoke) { Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "smoke-error.txt"), e.GetType().Name + ": " + e.Message); Environment.ExitCode = 1; }
            else MessageBox.Show("TypingStats не запущен.\r\n" + e.Message, "TypingStats", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { mutex.ReleaseMutex(); }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? cls, string title);
}

internal sealed class TrayHost : ApplicationContext
{
    private readonly Collector collector;
    private readonly MainForm form;
    private readonly NotifyIcon tray;
    private readonly HotkeyWindow hotkey;
    private readonly UpdateCoordinator updates;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly bool smoke;
    private bool closing;
    private bool lockWasPaused;
    private bool sessionLocked;
    private string? goalDay, backupDay;
    private int ticks;
    private readonly bool benchmark;
    private TimeSpan benchmarkCpu;
    private long benchmarkStart;
    public TrayHost(Collector collector, bool minimized, bool smoke, bool benchmark = false)
    {
        this.collector = collector; this.smoke = smoke; this.benchmark = benchmark; updates = new UpdateCoordinator(collector); form = new MainForm(collector,updates); MainForm = form;
        var menu = new ContextMenuStrip();
        Theme.Menu(menu);
        menu.Items.Add("Открыть статистику", null, (_, _) => Show());
        menu.Items.Add("Пауза / продолжить", null, async (_, _) => await Safe(collector.TogglePause()));
        menu.Items.Add("Сохранить сейчас", null, async (_, _) => await Safe(collector.SaveNow()));
        menu.Items.Add("Выход", null, (_, _) => Exit());
        tray = new NotifyIcon { Icon = AppIcons.Running, Text = "TypingStats — учёт работает", ContextMenuStrip = menu, Visible = true };
        updates.Notification += text => { if(!closing)tray.ShowBalloonTip(4000,"TypingStats — обновление",text,ToolTipIcon.Info); };
        updates.RestartRequested += Exit;
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Show(); };
        form.ExitRequested += Exit;
        hotkey = new HotkeyWindow(Show, () => _ = Safe(collector.TogglePause()), collector.Settings.HotkeyVk);
        _ = form.Handle;
        form.HandleCreated += (_, _) => hotkey.Target = form.Handle;
        hotkey.Target = form.Handle;
        Application.AddMessageFilter(new ActivationFilter(form));
        SystemEvents.PowerModeChanged += Power;
        SystemEvents.SessionSwitch += Session;
        timer.Tick += Tick; timer.Start();
        if (!benchmark && (!minimized || smoke)) Show();
        if (!hotkey.Registered && !smoke) tray.ShowBalloonTip(3000, "TypingStats", "Ctrl+Alt+F12 занят. Пауза доступна в трее.", ToolTipIcon.Info);
    }
    private void Show() { form.Show(); form.WindowState = FormWindowState.Normal; form.Activate(); form.RefreshData(); }
    private async Task Safe(Task task)
    { try { await task; } catch (Exception e) { tray.ShowBalloonTip(3000, "TypingStats", e.Message, ToolTipIcon.Warning); } }
    private async void Tick(object? sender, EventArgs e)
    {
        ticks++;
        if(!smoke&&!closing)
        {
            if(ticks%30==0 && collector.Settings.Theme==AppTheme.System && Theme.Preference==AppTheme.System)Theme.Set(AppTheme.System);
            _=updates.Tick(()=>AutomaticUpdatePolicy.CanInstall(collector.Settings.AutomaticUpdates,form.Visible,
                Application.OpenForms.Cast<Form>().Any(f=>f!=form&&f.Visible),sessionLocked||Native.GetForegroundWindow()==0,Native.IdleMilliseconds(),collector.StorageError==null&&!closing));
        }
        if (benchmark && ticks == 5)
        { using var p = System.Diagnostics.Process.GetCurrentProcess(); benchmarkCpu = p.TotalProcessorTime; benchmarkStart = Environment.TickCount64; }
        if (hotkey.Key != collector.Settings.HotkeyVk)
        {
            hotkey.Configure(collector.Settings.HotkeyVk);
            if (!hotkey.Registered) tray.ShowBalloonTip(3000, "TypingStats", "Выбранная горячая клавиша занята. Используйте трей.", ToolTipIcon.Info);
        }
        var statusIcon = collector.StorageError != null ? AppIcons.Error : collector.Paused ? AppIcons.Paused : AppIcons.Running;
        if (!ReferenceEquals(tray.Icon, statusIcon)) tray.Icon = statusIcon;
        tray.Text = collector.StorageError != null ? "TypingStats — не сохранено" : collector.Paused ? "TypingStats — пауза" : "TypingStats — учёт работает";
        if (smoke && ticks == (benchmark ? 35 : 3))
        {
            try
            {
                if (benchmark)
                {
                    using var p = System.Diagnostics.Process.GetCurrentProcess();
                    var elapsed = (Environment.TickCount64 - benchmarkStart) / 1000.0;
                    File.WriteAllText(Path.Combine(collector.Settings.DataDirectory, "benchmark-report.json"), JsonSerializer.Serialize(new
                    { Scenario = "background observation, 30 seconds after warmup; desktop input not controlled; not a full soak test", ElapsedSeconds = elapsed,
                        CpuPercentOfSystem = (p.TotalProcessorTime - benchmarkCpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100,
                        WorkingSetMB = p.WorkingSet64 / 1048576.0, PrivateMB = p.PrivateMemorySize64 / 1048576.0,
                        Processors = Environment.ProcessorCount, OS = Environment.OSVersion.VersionString, Runtime = Environment.Version.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
                    Show();
                }
                form.RefreshData(); using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(collector.Settings.DataDirectory, "smoke-ui.png"));
                form.RenderVerificationTabs(collector.Settings.DataDirectory);
                File.WriteAllText(Path.Combine(collector.Settings.DataDirectory, "smoke-report.json"), JsonSerializer.Serialize(new
                { UiRendered = true, Collector = collector.Status, Uia = collector.UiaStatus, Hotkey = hotkey.Registered, Database = collector.Store.Path,
                    CustomTrayIcon = ReferenceEquals(tray.Icon, AppIcons.Running) || ReferenceEquals(tray.Icon, AppIcons.Paused) || ReferenceEquals(tray.Icon, AppIcons.Error),
                    TrayIconWidth = tray.Icon!.Width, EmbeddedStateIcons = 3 }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(collector.Settings.DataDirectory, "smoke-error.txt"), error.ToString()); Environment.ExitCode = 1; }
            Exit(); return;
        }
        if (smoke || ticks % 30 != 0) return;
        var day = DateTime.Today.ToString("yyyy-MM-dd");
        var goalId = day + "-" + collector.Settings.Mode;
        if (collector.Settings.DailyGoal > 0 && goalDay != goalId)
        {
            var counters = TypingStats.Core.Counters.Sum(collector.Read(day, day, 2).Select(x => x.Counts));
            var keysMode = collector.Settings.Mode == TypingStats.Core.TrackingMode.Keys;
            var count = keysMode ? counters.KeyPresses : counters.Gross;
            if (count >= collector.Settings.DailyGoal)
            { goalDay = goalId; tray.ShowBalloonTip(3500, "Дневная цель выполнена", keysMode ? $"Сегодня {count:N0} нажатий клавиш." : $"Сегодня напечатано {count:N0} символов.", ToolTipIcon.Info); }
        }
        if (backupDay != day && collector.StorageError == null)
        {
            backupDay = day;
            try
            {
                var folder = Path.Combine(collector.Settings.DataDirectory, "backups"); Directory.CreateDirectory(folder);
                await collector.ChangeData(s => { s.Backup(Path.Combine(folder, day + ".sqlite")); s.Maintain(collector.Settings.MinuteDays); });
                // Only rotate our dated daily backups; migration/update/manual safety copies
                // must not disappear because another routine backup ran.
                foreach (var old in new DirectoryInfo(folder).GetFiles("*.sqlite")
                    .Where(f=>DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(f.Name),"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out _))
                    .OrderByDescending(f => f.Name).Skip(7)) old.Delete();
            }
            catch (Exception error) { tray.ShowBalloonTip(3000, "Резервная копия не создана", error.GetType().Name, ToolTipIcon.Warning); backupDay = null; }
        }
    }
    private void Power(object sender, PowerModeChangedEventArgs e) => _ = Safe(collector.BoundaryNow());
    private void Session(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) { sessionLocked=true;lockWasPaused = collector.Paused; _ = Safe(collector.SetPaused(true)); }
        else if (e.Reason == SessionSwitchReason.SessionUnlock) {sessionLocked=false;_ = Safe(collector.SetPaused(lockWasPaused));}
        else _ = Safe(collector.BoundaryNow());
    }
    private async void Exit()
    {
        if (closing || updates.Installing) return; closing = true;
        updates.Dispose();
        timer.Stop(); await Safe(collector.SetPaused(true));
        hotkey.Dispose(); tray.Visible = false; tray.Dispose();
        SystemEvents.PowerModeChanged -= Power; SystemEvents.SessionSwitch -= Session;
        form.AllowExit = true; form.Close(); ExitThread();
    }
    protected override void Dispose(bool disposing) { if (disposing) { updates.Dispose();timer.Dispose(); hotkey.Dispose(); tray.Dispose(); } base.Dispose(disposing); }
}

internal sealed class ActivationFilter(MainForm form) : IMessageFilter
{
    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != 0x8001 || m.HWnd != form.Handle) return false;
        form.Show(); form.WindowState = FormWindowState.Normal; form.Activate(); return true;
    }
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private readonly Action activate, toggle;
    public nint Target { get; set; }
    public bool Registered { get; private set; }
    public int Key { get; private set; }
    public HotkeyWindow(Action activate, Action toggle, int key)
    {
        this.activate = activate; this.toggle = toggle; CreateHandle(new CreateParams());
        Configure(key);
    }
    public void Configure(int key)
    { if (Registered) Native.UnregisterHotKey(Handle, 1); Key = key; Registered = Native.RegisterHotKey(Handle, 1, 0x4000 | 0x0001 | 0x0002, (uint)key); }
    protected override void WndProc(ref Message m)
    { if (m.Msg == 0x312) toggle(); else if (m.Msg == 0x8001) activate(); base.WndProc(ref m); }
    public void Dispose() { if (Handle != 0) { Native.UnregisterHotKey(Handle, 1); DestroyHandle(); } }
}

internal sealed class TestHost : Form
{
    public TestHost()
    {
        Text = "TypingStats — контрольное поле"; Size = new Size(700, 450); Font = new Font("Segoe UI", 11);
        var count = new Label { Dock = DockStyle.Bottom, Height = 45, Padding = new Padding(10) };
        var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Vertical };
        text.TextChanged += (_, _) => count.Text = $"Графем в контрольном поле: {new System.Globalization.StringInfo(text.Text).LengthInTextElements}. Это длина поля, включая вставки.";
        Controls.Add(text); Controls.Add(count);
        Controls.Add(new Label { Dock = DockStyle.Top, Height = 65, Padding = new Padding(10), Text = "Поле для ручной проверки RU/EN, IME, вставок и исправлений.\nТекст существует только в этом поле и не записывается в файл." });
    }
}
