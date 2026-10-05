using System.Drawing;

namespace TypingStats.Core;

public enum AppTheme { System = 0, Light = 1, Dark = 2 }
public sealed record ThemePalette(Color Page, Color Card, Color Ink, Color Muted, Color Line, Color Blue, Color BlueSoft, Color Green, Color Sidebar, Color ToggleOff)
{
    public static ThemePalette Light { get; } = new(Color.FromArgb(245,247,250), Color.White, Color.FromArgb(25,40,61), Color.FromArgb(93,108,129), Color.FromArgb(226,232,240), Color.FromArgb(43,104,216), Color.FromArgb(235,242,255), Color.FromArgb(22,143,130), Color.FromArgb(25,40,61), Color.FromArgb(211,220,232));
    public static ThemePalette Dark { get; } = new(Color.FromArgb(16,21,29), Color.FromArgb(24,31,42), Color.FromArgb(231,237,245), Color.FromArgb(157,174,197), Color.FromArgb(48,61,79), Color.FromArgb(109,168,255), Color.FromArgb(34,53,79), Color.FromArgb(68,203,174), Color.FromArgb(12,17,24), Color.FromArgb(65,80,101));
    public static bool IsDark(AppTheme preference, bool systemDark) => preference == AppTheme.Dark || preference == AppTheme.System && systemDark;
}

public static class AutomaticUpdatePolicy
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);
    public static bool CheckDue(bool enabled, DateTimeOffset now, DateTimeOffset startup, DateTimeOffset? lastCheck) =>
        enabled && now >= startup.AddSeconds(30) && (lastCheck == null || lastCheck > now || now - lastCheck >= CheckInterval);
    public static bool CanInstall(bool enabled, bool visibleWindow, bool dialogOpen, bool sessionLocked, long idleMs, bool storageHealthy) =>
        enabled && !visibleWindow && !dialogOpen && !sessionLocked && idleMs >= 60000 && storageHealthy;
    public static bool ShouldSuppress(string? attemptedVersion, Version installed) =>
        Version.TryParse(attemptedVersion, out var attempted) && UpdatePackage.Normalize(attempted) > UpdatePackage.Normalize(installed);
}
