using System.Text.Json;
using Microsoft.Win32;
using TypingStats.Core;

namespace TypingStats.App;

public sealed class Settings
{
    public int IdleSeconds { get; set; } = 5;
    public int SessionSeconds { get; set; } = 60;
    public int MinuteDays { get; set; } = 90;
    public int DailyGoal { get; set; }
    public bool StartMinimized { get; set; }
    public bool EnableCompositionResults { get; set; } = true;
    public int HotkeyVk { get; set; } = 0x7b;
    public TrackingMode Mode { get; set; } = TrackingMode.Both;
    public bool CountRepeats { get; set; } = true;
    public bool CountInjected { get; set; }
    public string DataDirectory { get; }
    private string FilePath => Path.Combine(DataDirectory, "settings.json");
    public Settings(string directory) { DataDirectory = directory; }
    public static Settings Load(string directory)
    {
        Directory.CreateDirectory(directory); var s = new Settings(directory);
        if (File.Exists(s.FilePath))
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(s.FilePath)); var r = json.RootElement;
                if (r.TryGetProperty("IdleSeconds", out var a)) s.IdleSeconds = Math.Clamp(a.GetInt32(), 1, 30);
                if (r.TryGetProperty("SessionSeconds", out a)) s.SessionSeconds = Math.Clamp(a.GetInt32(), 30, 600);
                if (r.TryGetProperty("MinuteDays", out a)) s.MinuteDays = Math.Clamp(a.GetInt32(), 1, 36500);
                if (r.TryGetProperty("DailyGoal", out a)) s.DailyGoal = Math.Max(0, a.GetInt32());
                if (r.TryGetProperty("StartMinimized", out a)) s.StartMinimized = a.GetBoolean();
                if (r.TryGetProperty("EnableCompositionResults", out a)) s.EnableCompositionResults = a.GetBoolean();
                if (r.TryGetProperty("HotkeyVk", out a)) s.HotkeyVk = Math.Clamp(a.GetInt32(), 0x70, 0x7b);
                if (r.TryGetProperty("Mode", out a) && a.TryGetInt32(out var mode) && Enum.IsDefined(typeof(TrackingMode), mode)) s.Mode = (TrackingMode)mode;
                if (r.TryGetProperty("CountRepeats", out a)) s.CountRepeats = a.GetBoolean();
                if (r.TryGetProperty("CountInjected", out a)) s.CountInjected = a.GetBoolean();
            }
            catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException)
            {
                // Preserve invalid settings; use defaults, never dump file content into a log.
                File.Copy(s.FilePath, s.FilePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), false);
            }
        }
        return s;
    }
    public void Save()
    {
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new { IdleSeconds, SessionSeconds, MinuteDays, DailyGoal, StartMinimized, EnableCompositionResults, HotkeyVk, Mode, CountRepeats, CountInjected }, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
    public static bool AutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("TypingStats") is string;
    }
    public static void AutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("TypingStats", "\"" + Environment.ProcessPath + "\" --minimized");
        else key.DeleteValue("TypingStats", false);
    }
}
