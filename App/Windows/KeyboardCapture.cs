using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TypingStats.App.Windows;

public sealed record KeyboardSample(uint Key, uint Scan, byte[] State, bool Repeat, bool Injected, uint Pid, nint Focus, nint Layout, DateTimeOffset Utc, long Mono, bool Extended = false);

internal sealed class KeyboardCapture : IDisposable
{
    private readonly Action<KeyboardSample> receive;
    private readonly Action mouse;
    private readonly Thread thread;
    private readonly byte[] state = new byte[256];
    private readonly bool[] down = new bool[256];
    private readonly Native.HookProc keyboardProc, mouseProc;
    private readonly ManualResetEventSlim ready = new();
    private uint threadId;
    private nint keyboardHook, mouseHook;
    private Exception? startupError;
    public KeyboardCapture(Action<KeyboardSample> receive, Action mouse)
    {
        this.receive = receive; this.mouse = mouse;
        keyboardProc = OnKeyboard; mouseProc = OnMouse;
        thread = new Thread(Run) { IsBackground = true, Name = "Keyboard capture" }; thread.Start();
        if (!ready.Wait(5000)) throw new TimeoutException("Не удалось запустить поток клавиатуры.");
        if (startupError != null) throw startupError;
    }
    private void Run()
    {
        try
        {
            threadId = Native.GetCurrentThreadId(); ResetState();
            keyboardHook = Native.SetWindowsHookEx(13, keyboardProc, Native.GetModuleHandle(null), 0);
            if (keyboardHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            mouseHook = Native.SetWindowsHookEx(14, mouseProc, Native.GetModuleHandle(null), 0);
            ready.Set();
            while (Native.GetMessage(out var msg, 0, 0, 0) > 0)
            {
                if (msg.Msg == 0x8002) ResetState();
                Native.TranslateMessage(ref msg); Native.DispatchMessage(ref msg);
            }
        }
        catch (Exception e) { startupError = e; ready.Set(); }
        finally { if (keyboardHook != 0) Native.UnhookWindowsHookEx(keyboardHook); if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook); }
    }
    private void ResetState()
    {
        Native.GetKeyboardState(state);
        for (var i = 0; i < down.Length; i++) down[i] = (state[i] & 128) != 0;
    }
    public void Resync() { if (threadId != 0) Native.PostThreadMessage(threadId, 0x8002, 0, 0); }
    private nint OnKeyboard(int code, nint wp, nint lp)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<Native.Kbd>(lp);
            if (k.Vk < 256)
            {
                var pressed = wp == 0x100 || wp == 0x104; var released = wp == 0x101 || wp == 0x105;
                if (pressed || released)
                {
                    var vk = (int)k.Vk; var repeat = down[vk]; var injected = (k.Flags & 0x10) != 0;
                    // Injected macros must not corrupt physical modifier state.
                    if (!injected)
                    {
                        down[vk] = pressed;
                        if (pressed && !repeat && vk is 0x14 or 0x90 or 0x91) state[vk] ^= 1;
                        state[vk] = (byte)((state[vk] & 1) | (pressed ? 128 : 0));
                        state[0x10] = (byte)((down[0xa0] || down[0xa1]) ? 128 : 0);
                        state[0x11] = (byte)((down[0xa2] || down[0xa3]) ? 128 : 0);
                        state[0x12] = (byte)((down[0xa4] || down[0xa5]) ? 128 : 0);
                    }
                    if (pressed)
                    {
                        var window = Native.GetForegroundWindow(); var tid = Native.GetWindowThreadProcessId(window, out var pid);
                        var gui = new Native.GuiInfo { Size = (uint)Marshal.SizeOf<Native.GuiInfo>() };
                        Native.GetGUIThreadInfo(tid, ref gui);
                        if(gui.Focus!=0){var focusedThread=Native.GetWindowThreadProcessId(gui.Focus,out var focusPid);if(focusPid!=0){pid=focusPid;if(focusedThread!=0)tid=focusedThread;}}
                        receive(new KeyboardSample(k.Vk, k.Scan, (byte[])state.Clone(), repeat, injected, pid,
                            gui.Focus != 0 ? gui.Focus : window, Native.GetKeyboardLayout(tid), DateTimeOffset.UtcNow, Environment.TickCount64, (k.Flags & 1) != 0));
                    }
                }
            }
        }
        return Native.CallNextHookEx(keyboardHook, code, wp, lp);
    }
    private nint OnMouse(int code, nint wp, nint lp)
    {
        if (code >= 0 && (wp == 0x201 || wp == 0x204)) mouse();
        return Native.CallNextHookEx(mouseHook, code, wp, lp);
    }
    public void Dispose()
    {
        if (threadId != 0) Native.PostThreadMessage(threadId, 0x12, 0, 0);
        thread.Join(2000); ready.Dispose();
    }
}
