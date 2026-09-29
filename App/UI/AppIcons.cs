using System.Reflection;

namespace TypingStats.App.UI;

/// <summary>Application-lifetime cached HICONs; never allocate new icons on the tray timer.</summary>
internal static class AppIcons
{
    public static Icon Running { get; } = Load("typingstats", SystemInformation.SmallIconSize);
    public static Icon Paused { get; } = Load("typingstats-paused", SystemInformation.SmallIconSize);
    public static Icon Error { get; } = Load("typingstats-error", SystemInformation.SmallIconSize);
    public static Icon Application { get; } = Load("typingstats", new Size(48, 48));

    private static Icon Load(string name, Size size)
    {
        using var stream = typeof(AppIcons).Assembly.GetManifestResourceStream("TypingStats.Assets." + name + ".ico")
            ?? throw new InvalidOperationException("Missing application icon: " + name);
        using var icon = new Icon(stream, size);
        return (Icon)icon.Clone();
    }
}
