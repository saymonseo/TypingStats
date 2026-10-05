using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using TypingStats.App.Windows;
using TypingStats.Core;
using TypingStats.Storage;

namespace TypingStats.App;

public sealed class Collector : IDisposable
{
    private sealed record KeyEvent(KeyboardSample Sample, FocusSnapshot? Focus);
    private sealed record Command(Action Action, TaskCompletionSource Completion);
    private sealed record Boundary;
    private readonly BlockingCollection<object> queue = new(8192);
    private readonly TypingEngine engine = new();
    private readonly List<FlushBatch> retries = new();
    private readonly object viewGate = new();
    private readonly StatsStore store;
    private readonly Settings settings;
    private readonly Thread worker;
    private readonly CompositionObserver observer;
    private readonly KeyboardCapture keyboard;
    private readonly ApplicationResolver appResolver;
    private readonly Dictionary<string, (InputContext Context, long LastKey, bool Active, bool Ime)> compositions = new();
    private InputContext current = new("unknown", "unknown", "unknown");
    private volatile bool paused, running = true;
    private long dropped, lastFlush, lastForegroundCheck;
    private uint lastForeground;
    private TrackingMode appliedMode;
    private string? storageError;
    private string status = "Учёт работает";
    private string RecoveryPath => Path.Combine(settings.DataDirectory, "pending-aggregates.json");
    public bool Paused => paused;
    public string Status => paused && Volatile.Read(ref status) == "Учёт работает" ? "Пауза" : Volatile.Read(ref status);
    public string UiaStatus => observer.Status;
    public string? StorageError => Volatile.Read(ref storageError);
    public Settings Settings => settings;
    public StatsStore Store => store;
    public Collector(Settings settings, StatsStore store)
    {
        this.settings = settings; this.store = store; appliedMode = settings.Mode;
        appResolver=new ApplicationResolver(store);
        if (File.Exists(RecoveryPath))
        {
            var recovered = JsonSerializer.Deserialize<List<FlushBatch>>(File.ReadAllText(RecoveryPath)) ?? [];
            foreach (var batch in recovered) store.Save(batch);
            File.Delete(RecoveryPath);
        }
        observer = new CompositionObserver(sample => Enqueue(sample));
        observer.Enabled = settings.Mode != TrackingMode.Keys;
        keyboard = new KeyboardCapture(sample =>
        {
            if (paused) { Array.Clear(sample.State); return; }
            if (!queue.TryAdd(new KeyEvent(sample, observer.Focus))) { Array.Clear(sample.State); Interlocked.Increment(ref dropped); }
        }, sample => Enqueue(sample));
        worker = new Thread(Work) { IsBackground = true, Name = "Typing engine and storage" }; worker.Start();
    }
    private void Enqueue(object value)
    { if (running && !paused && !queue.TryAdd(value)) Interlocked.Increment(ref dropped); }
    private void Work()
    {
        lastFlush = Environment.TickCount64;
        while (running || queue.Count > 0)
        {
            object? item = null;
            try
            {
                queue.TryTake(out item, 100);
                lock (viewGate)
                {
                    var now = DateTimeOffset.UtcNow; var mono = Environment.TickCount64;
                    engine.IdleSeconds = settings.IdleSeconds; engine.SessionSeconds = settings.SessionSeconds;
                    observer.Enabled = settings.Mode != TrackingMode.Keys;
                    if (appliedMode != settings.Mode)
                    { ResolveOutstanding(); engine.Stop(now, mono, "mode-change"); Flush(); appliedMode = settings.Mode; }
                    if (item is Command command)
                    {
                        try { command.Action(); command.Completion.SetResult(); }
                        catch (Exception e) { command.Completion.SetException(e); }
                    }
                    else if (!paused)
                    {
                        if (item is KeyEvent key) ProcessKey(key);
                        else if (item is MouseSample mouse) ProcessMouse(mouse);
                        else if (item is CompositionSample sample) ProcessComposition(sample);
                        else if (item is Boundary) engine.BreakSeries();
                        var lost = Interlocked.Exchange(ref dropped, 0);
                        if (lost > 0) { engine.Lost(current, now, lost); ResolveOutstanding(); keyboard.Resync(); }
                        if (mono - lastForegroundCheck > 300)
                        {
                            var hwnd = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(hwnd, out var pid);
                            if (lastForeground != 0 && pid != lastForeground) engine.EndContext(now, mono);
                            lastForeground = pid; lastForegroundCheck = mono;
                        }
                        engine.Advance(now, mono);
                        foreach (var pair in compositions.ToArray())
                            if (mono - pair.Value.LastKey > 60000)
                            { if (pair.Value.Active || pair.Value.Ime) engine.Unresolved(pair.Value.Context, now); compositions.Remove(pair.Key); }
                    }
                    if (mono - lastFlush >= 5000)
                    { Flush(); lastFlush = mono; }
                }
            }
            catch (Exception e)
            {
                Volatile.Write(ref status, "Ошибка обработки: " + e.GetType().Name);
                lock (viewGate) engine.BreakSeries();
            }
            finally { if (item is KeyEvent key) Array.Clear(key.Sample.State); }
        }
        lock (viewGate)
        {
            ResolveOutstanding(); engine.Stop(DateTimeOffset.UtcNow, Environment.TickCount64, "exit"); Flush();
            if (retries.Count > 0) File.WriteAllText(RecoveryPath, JsonSerializer.Serialize(retries));
        }
    }
    private static string Profile(nint layout)
    {
        var id = (int)((long)layout & 0xffff);
        try { return CultureInfo.GetCultureInfo(id).Name + " / " + ((long)layout).ToString("X"); }
        catch (CultureNotFoundException) { return "HKL " + ((long)layout).ToString("X"); }
    }
    private void ProcessKey(KeyEvent item)
    {
        var s = item.Sample; var focus = item.Focus;
        var token = focus?.Pid == s.Pid ? focus.Token : s.Pid + ":hwnd:" + s.Focus;
        current = new InputContext(appResolver.Resolve(s.Pid,s.Utc), Profile(s.Layout), token);
        engine.Key(current, s.Utc, s.Mono, s.Repeat, s.Injected);
        if (KeyIdentity.CountPress(settings.Mode, s.Repeat, s.Injected, settings.CountRepeats, settings.CountInjected))
            engine.KeyPress(current, s.Utc, s.Mono, KeyIdentity.Code(s.Scan, s.Extended, s.Key), KeyIdentity.Label(s.Scan, s.Extended, s.Key), s.Repeat, s.Injected);
        if (settings.Mode == TrackingMode.Keys) return;
        if (s.Injected) { engine.BreakSeries(); return; }
        var vk = s.Key; var ctrl = (s.State[0x11] & 128) != 0; var alt = (s.State[0x12] & 128) != 0;
        var altGr = (s.State[0xa5] & 128) != 0 && ctrl;
        var win = (s.State[0x5b] & 128) != 0 || (s.State[0x5c] & 128) != 0;
        var shift = (s.State[0x10] & 128) != 0;
        if (vk is >= 0xa0 and <= 0xa5 or 0x10 or 0x11 or 0x12 or 0x14 or 0x90 or 0x91) return;
        if ((ctrl && !altGr) || win || (alt && !altGr))
        {
            var action = vk switch
            {
                0x56 when ctrl => EditAction.Paste,
                0x08 or 0x2e when ctrl => EditAction.WordDelete,
                0x58 when ctrl => EditAction.Cut,
                0x5a when ctrl && !shift => EditAction.Undo,
                0x59 when ctrl => EditAction.Redo,
                0x5a when ctrl && shift => EditAction.Redo,
                _ => EditAction.Navigation
            };
            if (compositions.Remove(token, out var pending) && (pending.Active || pending.Ime)) engine.Unresolved(pending.Context, s.Utc);
            engine.Edit(current, s.Utc, s.Mono, action); return;
        }
        if (vk == 0x2d && shift)
        {
            if (compositions.Remove(token, out var pending) && (pending.Active || pending.Ime)) engine.Unresolved(pending.Context, s.Utc);
            engine.Edit(current, s.Utc, s.Mono, EditAction.Paste); return;
        }
        if (vk is 0x08 or 0x2e)
        {
            var composing = compositions.TryGetValue(token, out var entry) && (entry.Active || entry.Ime);
            if (composing) engine.Edit(current, s.Utc, s.Mono, EditAction.Navigation);
            else engine.Edit(current, s.Utc, s.Mono, vk == 0x08 ? EditAction.Backspace : EditAction.Delete);
            return;
        }
        if (vk is 0x1b or >= 0x21 and <= 0x28 or 0x09 or 0x0d or >= 0x70 and <= 0x87)
        {
            engine.Edit(current, s.Utc, s.Mono, EditAction.Navigation); return;
        }
        var isIme = Native.IsIme(s.Layout);
        if (isIme || (compositions.TryGetValue(token, out var composition) && composition.Active))
        {
            var active = compositions.TryGetValue(token, out var previous) && previous.Active;
            compositions[token] = (current, s.Mono, active, true);
            return; // No transliterated keystrokes counted as committed IME characters.
        }
        compositions[token] = (current, s.Mono, false, false);
        var output = new char[16];
        try
        {
            var n = Native.ToUnicodeEx(vk, s.Scan, s.State, output, output.Length, 4, s.Layout);
            if (n < 0) { engine.Unresolved(current, s.Utc); return; }
            if (n > 0)
            {
                // Untested controls remain explicitly keyboard-estimated, never result-observed.
                engine.Text(current, s.Utc, s.Mono, output.AsSpan(0, Math.Min(n, output.Length)), false);
            }
        }
        finally { Array.Clear(output); }
    }
    private void ProcessComposition(CompositionSample sample)
    {
        if (settings.Mode == TrackingMode.Keys || !settings.EnableCompositionResults) return;
        if (!compositions.TryGetValue(sample.Token, out var entry))
        {
            if (sample.Finished) engine.Unresolved(new InputContext(appResolver.Resolve((uint)sample.Pid,sample.Utc), "UIA / unknown", sample.Token), sample.Utc);
            return;
        }
        if (sample.Mono < entry.LastKey || sample.Mono - entry.LastKey > 60000) return;
        if (sample.Started)
        {
            // Ordinary-key estimates cannot be safely replaced without a tested provider.
            if (entry.Ime) { compositions[sample.Token] = entry with { Active = true }; engine.BreakSeries(); }
            return;
        }
        if (!sample.Finished) return;
        // Conservatively require an IME route AND a preceding composition event.
        // Emoji/software-panel provenance is not claimed by this first adapter.
        if (!entry.Ime || !entry.Active || sample.Overflow)
        { engine.Unresolved(entry.Context, sample.Utc); compositions.Remove(sample.Token); return; }
        engine.CommitCount(entry.Context, sample.Utc, sample.Mono, sample.Count, sample.White);
        compositions.Remove(sample.Token);
    }
    private void ProcessMouse(MouseSample sample)
    {
        engine.BreakSeries();
        if (MouseStatistics.CountPress(settings.TrackMouse, sample.Injected, settings.CountMouseInjected))
            engine.MousePress(appResolver.Resolve(sample.Pid, sample.Utc), sample.Utc, sample.Button, sample.Injected);
    }
    private void ResolveOutstanding()
    {
        foreach (var e in compositions.Values) if (e.Active || e.Ime) engine.Unresolved(e.Context, DateTimeOffset.UtcNow);
        compositions.Clear();
    }
    private void Flush()
    {
        var batch = engine.Drain(); if (batch.Rows.Count != 0 || batch.Sessions.Count != 0 || (batch.Keys?.Count ?? 0) != 0 || (batch.Mouse?.Count ?? 0) != 0) retries.Add(batch);
        try
        {
            while (retries.Count > 0) { store.Save(retries[0]); retries.RemoveAt(0); }
            Volatile.Write(ref storageError, null);
        }
        catch (Exception e)
        {
            Volatile.Write(ref storageError, "Не сохранено: " + e.GetType().Name);
            if (retries.Sum(b => b.Rows.Count + (b.Keys?.Count ?? 0) + (b.Mouse?.Count ?? 0)) * 400L > 16 * 1024 * 1024)
            { paused = true; Volatile.Write(ref status, "Учёт остановлен: хранилище"); }
        }
    }
    private Task Invoke(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryAdd(new Command(action, tcs), 2000)) tcs.SetException(new IOException("Очередь коллектора занята."));
        return tcs.Task;
    }
    public Task TogglePause() => SetPaused(!paused);
    public Task SetPaused(bool value) => Invoke(() =>
    {
        paused = value; ResolveOutstanding(); engine.Stop(DateTimeOffset.UtcNow, Environment.TickCount64, "pause");
        keyboard.Resync(); Flush(); Volatile.Write(ref status, value ? "Пауза" : "Учёт работает");
    });
    public Task SaveNow() => Invoke(Flush);
    public Task SetMode(TrackingMode mode) => Invoke(() =>
    { ResolveOutstanding(); engine.Stop(DateTimeOffset.UtcNow, Environment.TickCount64, "mode-change"); Flush(); settings.Mode = mode; appliedMode = mode; settings.Save(); });
    /// <summary>Controlled metadata pipeline test; never injects keys into another window.</summary>
    public Task VerifyPipeline() => Invoke(() =>
    {
        paused = true;
        if (store.Read("2000-01-01", "9999-12-31", 2).Count != 0) throw new InvalidOperationException("Проверка требует пустую тестовую базу.");
        var utc = DateTimeOffset.UtcNow; var mono = Environment.TickCount64; var pid = (uint)Environment.ProcessId;
        var focus = new FocusSnapshot((int)pid, "verification-field", true, false);
        var layouts = new nint[Math.Max(1, Native.GetKeyboardLayoutList(0, null))]; Native.GetKeyboardLayoutList(layouts.Length, layouts);
        var layout = layouts.FirstOrDefault(l => ((long)l & 0x3ff) == 9);
        if (layout == 0) layout = layouts.FirstOrDefault(l => !Native.IsIme(l));
        if (layout == 0) throw new InvalidOperationException("Для проверки нужна загруженная обычная раскладка.");
        var emptyState = new byte[256];
        void Key(uint vk, byte[]? state = null)
        {
            ProcessKey(new KeyEvent(new KeyboardSample(vk, Native.MapVirtualKeyEx(vk, 0, layout), state ?? emptyState, false, false, pid, 0, layout, utc, mono++), focus));
        }
        Key(0x41); Key(0x42); Key(0x43); Key(0x08);
        var ctrl = new byte[256]; ctrl[0x11] = 128; Key(0x56, ctrl); Key(0x08);
        compositions[focus.Token] = (current, mono, false, true);
        ProcessComposition(new CompositionSample(focus.Token, (int)pid, true, false, 0, 0, false, utc, mono++));
        ProcessComposition(new CompositionSample(focus.Token, (int)pid, false, true, 2, 0, false, utc, mono++));
        // A pending composition must not allow a later paste payload to count as typing.
        compositions[focus.Token] = (current, mono, true, true);
        Key(0x56, ctrl);
        ProcessComposition(new CompositionSample(focus.Token, (int)pid, false, true, 999, 0, false, utc, mono++));
        engine.Stop(utc, mono, "verification"); Flush();
        var result = Counters.Sum(store.Read("2000-01-01", "9999-12-31", 2).Select(r => r.Counts));
        if (result.Estimated != 3 || result.Observed != 2 || result.LinkedBackspaces != 1 || result.Pastes != 2)
            throw new InvalidOperationException($"Pipeline mismatch: estimated={result.Estimated}, observed={result.Observed}, linked={result.LinkedBackspaces}, paste={result.Pastes}");
        var baselineKeys = result.KeyPresses;
        settings.Mode = TrackingMode.Keys; appliedMode = TrackingMode.Keys; settings.CountRepeats = false;
        void Plain(uint vk, uint scan, bool extended = false, bool repeat = false, bool injected = false)
        { ProcessKey(new KeyEvent(new KeyboardSample(vk, scan, emptyState, repeat, injected, pid, 0, layout, utc, mono++, extended), null)); }
        // No editable field: game controls, shortcuts, modifiers and function keys still count.
        Plain(0x57,0x11); Plain(0x41,0x1e); Plain(0x53,0x1f); Plain(0x44,0x20); Plain(0x20,0x39);
        Plain(0xa0,0x2a); Plain(0xa3,0x1d,true); Plain(0x0d,0x1c); Plain(0x0d,0x1c,true);
        Plain(0x5b,0x5b,true); Plain(0x70,0x3b); Plain(0x1b,0x01);
        Plain(0x57,0x11,repeat:true); Plain(0xe7,0x042f,injected:true);
        settings.CountInjected = true; Plain(0xe7,0x042f,injected:true);
        settings.CountInjected = false; settings.CountRepeats = true;
        engine.Stop(utc,mono,"key-policy-verification"); Flush();
        var keyResult = Counters.Sum(store.Read("2000-01-01","9999-12-31",2).Select(r => r.Counts));
        if (keyResult.KeyPresses != baselineKeys + 13 || keyResult.Gross != result.Gross || store.ReadKeys("2000-01-01","9999-12-31").Sum(k => k.Presses) != keyResult.KeyPresses)
            throw new InvalidOperationException("All-key mode / repeat / injected policy mismatch");
        foreach (var button in Enum.GetValues<MouseButton>()) ProcessMouse(new MouseSample(button, false, pid, utc));
        ProcessMouse(new MouseSample(MouseButton.Left, false, pid, utc)); ProcessMouse(new MouseSample(MouseButton.Left, false, pid, utc));
        ProcessMouse(new MouseSample(MouseButton.Right, true, pid, utc)); // ignored by default
        settings.CountMouseInjected = true; ProcessMouse(new MouseSample(MouseButton.X2, true, pid, utc)); settings.CountMouseInjected = false;
        settings.TrackMouse = false; ProcessMouse(new MouseSample(MouseButton.Left, false, pid, utc)); settings.TrackMouse = true;
        foreach (var mode in Enum.GetValues<TrackingMode>()) { settings.Mode = mode; ProcessMouse(new MouseSample(MouseButton.Middle, false, pid, utc)); }
        settings.Mode = TrackingMode.Keys; appliedMode = TrackingMode.Keys; Flush();
        var mouseRows = ReadMouse("2000-01-01", "9999-12-31");
        var afterMouse = Counters.Sum(Read("2000-01-01", "9999-12-31", 2).Select(r => r.Counts));
        if (mouseRows.Sum(r => r.Presses) != 11 || mouseRows.Sum(r => r.Injected) != 1 || mouseRows.Select(r => r.Button).Distinct().Count() != 5
            || afterMouse.KeyPresses != keyResult.KeyPresses || afterMouse.Gross != keyResult.Gross)
            throw new InvalidOperationException("Mouse pipeline / independent counts mismatch");
        File.WriteAllText(Path.Combine(settings.DataDirectory, "pipeline-report.json"), JsonSerializer.Serialize(new
        { Test = "controlled metadata (not physical keyboard or mouse / live game compatibility)", Passed = true, Estimated = result.Estimated, Observed = result.Observed, Net = result.Net, Pastes = result.Pastes, KeyModePassed = true, KeyPresses = keyResult.KeyPresses, NoTextAddedInKeyMode = keyResult.Gross == result.Gross, MousePipelinePassed = true, MousePresses = mouseRows.Sum(r => r.Presses) }, new JsonSerializerOptions { WriteIndented = true }));
    });
    public Task BoundaryNow() => Invoke(() =>
    { ResolveOutstanding(); engine.Stop(DateTimeOffset.UtcNow, Environment.TickCount64, "system"); keyboard.Resync(); Flush(); });
    public Task ChangeData(Action<StatsStore> action) => Invoke(() =>
    {
        var previous = paused; paused = true;
        try
        {
            ResolveOutstanding(); engine.Stop(DateTimeOffset.UtcNow, Environment.TickCount64, "data-operation"); Flush();
            if (storageError != null) throw new IOException(storageError);
            action(store);
        }
        finally { paused = previous; keyboard.Resync(); }
    });
    public IReadOnlyList<MetricRow> Read(string start, string end, int resolution)
    {
        lock (viewGate)
        {
            var rows = store.Read(start, end, resolution).ToList();
            foreach (var row in retries.SelectMany(b => b.Rows).Concat(engine.Peek()))
                if (string.CompareOrdinal(row.Key.LocalDate, start) >= 0 && string.CompareOrdinal(row.Key.LocalDate, end) <= 0) rows.Add(row);
            return rows;
        }
    }
    public IReadOnlyList<KeyMetricRow> ReadKeys(string start, string end)
    {
        lock (viewGate)
        {
            var rows = store.ReadKeys(start, end).ToList();
            foreach (var row in retries.SelectMany(b => b.Keys ?? []).Concat(engine.PeekKeys()))
                if (string.CompareOrdinal(row.Key.LocalDate, start) >= 0 && string.CompareOrdinal(row.Key.LocalDate, end) <= 0) rows.Add(row);
            return rows;
        }
    }
    public void Dispose()
    {
        paused = true; keyboard.Dispose(); observer.Dispose(); running = false;
        if (!worker.Join(5000)) throw new IOException("Не удалось завершить сохранение статистики.");
        queue.Dispose(); appResolver.Dispose(); store.Dispose();
    }
    public IReadOnlyList<MouseMetricRow> ReadMouse(string start, string end)
    {
        lock (viewGate)
        {
            var rows = store.ReadMouse(start, end).ToList();
            rows.AddRange(retries.SelectMany(b => b.Mouse ?? []).Concat(engine.PeekMouse())
                .Where(r => string.CompareOrdinal(r.LocalDate, start) >= 0 && string.CompareOrdinal(r.LocalDate, end) <= 0));
            return rows;
        }
    }
}
