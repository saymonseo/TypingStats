using System.Runtime.InteropServices;

namespace TypingStats.App.Windows;

internal static class Native
{
    internal delegate nint HookProc(int code, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] internal struct Kbd { public uint Vk, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiInfo
    { public uint Size, Flags; public nint Active, Focus, Capture, Menu, Move, Caret; public Rect CaretRect; }
    [StructLayout(LayoutKind.Sequential)] internal struct Message
    { public nint Hwnd; public uint Msg; public nuint WParam; public nint LParam; public uint Time; public Point Pt; public uint Private; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll")] internal static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] internal static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);
    [DllImport("user32.dll")] internal static extern uint MapVirtualKeyEx(uint code, uint type, nint layout);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [DllImport("user32.dll")] internal static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll")] internal static extern short GetKeyState(int key);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, [Out] char[] output, int size, uint flags, nint layout);
    [DllImport("imm32.dll")] internal static extern bool ImmIsIME(nint layout);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] internal static extern int GetMessage(out Message msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Message msg);
    [DllImport("user32.dll")] internal static extern nint DispatchMessage(ref Message msg);
    [DllImport("user32.dll")] internal static extern bool PostThreadMessage(uint thread, uint msg, nuint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(nint hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint hwnd, uint msg, nint wp, nint lp);
    internal static bool IsIme(nint layout)
    {
        var language = (int)((long)layout & 0x3ff);
        // ImmIsIME also reports TRUE for ordinary TSF keyboards on this Windows build.
        // Use composition-language profiles / the legacy IME HKL marker conservatively.
        var highWord = ((ulong)(long)layout >> 16) & 0xffff;
        return language is 0x04 or 0x11 or 0x12 || (highWord & 0xf000) == 0xe000;
    }
}
