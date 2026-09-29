using System.Runtime.InteropServices;
using Interop.UIAutomationClient;
using TypingStats.Core;

namespace TypingStats.App.Windows;

public sealed record FocusSnapshot(int Pid, string Token, bool? Editable, bool HasTextEdit);
public sealed record CompositionSample(string Token, int Pid, bool Started, bool Finished, int Count, int White, bool Overflow, DateTimeOffset Utc, long Mono);

internal sealed class CompositionObserver : IDisposable
{
    private readonly Thread thread;
    private readonly ManualResetEventSlim stop = new();
    private readonly AutoResetEvent focusChanged = new(false);
    private readonly Action<CompositionSample> emit;
    private volatile FocusSnapshot? focus;
    private string status = "Инициализация UI Automation";
    private int enabled = 1;
    public bool Enabled
    {
        get => Volatile.Read(ref enabled) != 0;
        set { var next = value ? 1 : 0; if (Interlocked.Exchange(ref enabled, next) != next) focusChanged.Set(); }
    }
    public FocusSnapshot? Focus => focus;
    public string Status => Volatile.Read(ref status);
    public CompositionObserver(Action<CompositionSample> emit)
    {
        this.emit = emit; thread = new Thread(Run) { IsBackground = true, Name = "UI Automation (MTA)" };
        thread.SetApartmentState(ApartmentState.MTA); thread.Start();
    }
    private void Run()
    {
        IUIAutomation3? automation = null; IUIAutomationElement? element = null; Handler? handler = null;
        string? token = null; FocusWake? wakeHandler = null;
        try
        {
            automation = (IUIAutomation3)new CUIAutomation8();
            wakeHandler = new FocusWake(focusChanged);
            automation.AddFocusChangedEventHandler(null!, wakeHandler);
            while (!stop.IsSet)
            {
                if (!Enabled)
                {
                    handler?.Invalidate();
                    if (element != null && handler != null) try { automation.RemoveTextEditTextChangedEventHandler(element, handler); } catch (COMException) { }
                    Release(element); element = null; handler = null; token = null; focus = null;
                    Volatile.Write(ref status, "Режим клавиш: текстовые события не используются"); WaitForFocus(); continue;
                }
                try
                {
                    var current = automation.GetFocusedElement();
                    if (current == null) { focus = null; WaitForFocus(); continue; }
                    var id = string.Join(".", current.GetRuntimeId()); var pid = current.CurrentProcessId;
                    var newToken = pid + ":" + id;
                    if (newToken != token)
                    {
                        if (element != null && handler != null)
                        {
                            handler.Invalidate();
                            try { automation.RemoveTextEditTextChangedEventHandler(element, handler); } catch (COMException) { }
                        }
                        Release(element); element = current; token = newToken;
                        var type = element.CurrentControlType;
                        var hasText = IsAvailable(element, 30149);
                        bool? editable = type is 50004 or 50030 ? true : null;
                        handler = new Handler(newToken, pid, emit);
                        if (hasText)
                        {
                            automation.AddTextEditTextChangedEventHandler(element, TreeScope.TreeScope_Element, TextEditChangeType.TextEditChangeType_Composition, null!, handler);
                            automation.AddTextEditTextChangedEventHandler(element, TreeScope.TreeScope_Element, TextEditChangeType.TextEditChangeType_CompositionFinalized, null!, handler);
                        }
                        focus = new FocusSnapshot(pid, newToken, editable, hasText);
                        Volatile.Write(ref status, hasText ? "Поле сообщает TextEdit / IME" : "Оценка по клавишам; TextEdit недоступен");
                    }
                    else Release(current);
                }
                catch (Exception e) when (e is COMException or InvalidCastException or InvalidOperationException)
                {
                    focus = null; token = null;
                    Volatile.Write(ref status, "UIA недоступен; клавиатурная оценка работает");
                }
                WaitForFocus();
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        { Volatile.Write(ref status, "UI Automation не запущен (" + e.GetType().Name + ")"); }
        finally
        {
            handler?.Invalidate();
            wakeHandler?.Invalidate();
            if (automation != null) try { automation.RemoveAllEventHandlers(); } catch (COMException) { }
            Release(element); Release(automation); focus = null;
        }
    }
    private void WaitForFocus() => WaitHandle.WaitAny([stop.WaitHandle, focusChanged], 3000);
    [ComVisible(true)]
    private sealed class FocusWake(AutoResetEvent changed) : IUIAutomationFocusChangedEventHandler
    {
        private int valid = 1;
        public void Invalidate() => Interlocked.Exchange(ref valid, 0);
        public void HandleFocusChangedEvent(IUIAutomationElement sender)
        { if (Volatile.Read(ref valid) != 0) changed.Set(); }
    }
    private static bool IsAvailable(IUIAutomationElement e, int id)
    { try { return e.GetCurrentPropertyValue(id) is bool b && b; } catch (COMException) { return false; } }
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    [ComVisible(true)]
    private sealed class Handler : IUIAutomationTextEditTextChangedEventHandler
    {
        private readonly string token;
        private readonly int pid;
        private readonly Action<CompositionSample> emit;
        private int valid = 1;
        public Handler(string token, int pid, Action<CompositionSample> emit) { this.token = token; this.pid = pid; this.emit = emit; }
        public void Invalidate() => Interlocked.Exchange(ref valid, 0);
        public void HandleTextEditTextChangedEvent(IUIAutomationElement sender, TextEditChangeType kind, string[] strings)
        {
            if (Volatile.Read(ref valid) == 0) return;
            var utc = DateTimeOffset.UtcNow; var mono = Environment.TickCount64;
            if (kind == TextEditChangeType.TextEditChangeType_Composition)
            { emit(new(token, pid, true, false, 0, 0, false, utc, mono)); return; }
            if (kind != TextEditChangeType.TextEditChangeType_CompositionFinalized) return;
            if (strings.Sum(s => (long)(s?.Length ?? 0)) > 4096)
            { emit(new(token, pid, false, true, 0, 0, true, utc, mono)); return; }
            var result = string.Concat(strings); var counter = new GraphemeStream(); var parsed = counter.Append(result); counter.Reset();
            emit(new(token, pid, false, true, parsed.Delta, parsed.White, parsed.Overflow, utc, mono));
        }
    }
    public void Dispose()
    { stop.Set(); if (thread.Join(2000)) { stop.Dispose(); focusChanged.Dispose(); } }
}
