using Microsoft.Win32;
using TypingStats.Core;

namespace TypingStats.App.Windows;

internal interface IStartupStore
{
    string? Read();
    void Write(string command);
    void Remove();
}

internal sealed class RegistryStartupStore(string keyPath = RegistryStartupStore.RunKey) : IStartupStore
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TypingStats";
    public string? Read() { using var key = Registry.CurrentUser.OpenSubKey(keyPath); return key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string; }
    public void Write(string command) { using var key = Registry.CurrentUser.CreateSubKey(keyPath); key.SetValue(ValueName, command, RegistryValueKind.String); }
    public void Remove() { using var key = Registry.CurrentUser.OpenSubKey(keyPath, true); key?.DeleteValue(ValueName, false); }
}

internal static class WindowsStartup
{
    public static string Executable()
    {
        var executable = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(executable) && !Path.GetFileName(executable).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return executable;
        var host = Path.Combine(AppContext.BaseDirectory, "TypingStats.exe");
        if (File.Exists(host)) return host;
        throw new IOException("Для автозапуска нужна полная сборка с TypingStats.exe.");
    }
    public static void Synchronize(bool enabled, string executable, string dataDirectory, IStartupStore store)
    {
        var existing = store.Read();
        if (!enabled) { if (existing != null) store.Remove(); return; }
        var desired = StartupCommand.Build(executable, dataDirectory);
        if (!string.Equals(existing, desired, StringComparison.Ordinal)) store.Write(desired);
        // StartupApproved belongs to Windows/Task Manager. Never override that separate user choice.
    }
}
