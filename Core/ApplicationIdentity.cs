namespace TypingStats.Core;

public sealed record ApplicationIdentity(string Id, string Name, string Executable, string ImageFingerprint)
{
    public string Caption => (Name.Equals(Executable, StringComparison.OrdinalIgnoreCase) ? Executable : Name + " · " + Executable) + (Id.Contains('@') ? " ["+Id.Split('@')[^1]+"]" : "");
    public static ApplicationIdentity Unknown(string id) => new(id, id == "unknown" ? "Не удалось определить" : id.Split('@')[0], id.Split('@')[0], "");
}
