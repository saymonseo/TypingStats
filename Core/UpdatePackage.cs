using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace TypingStats.Core;

public sealed record ReleaseUpdate(Version Version, string Tag, string Name, Uri Download, string Sha256, long Size, bool Preview);
public sealed record UpdateJob(string Id, int ParentPid, string SourceDirectory, string TargetDirectory, string DataFile, string DataBackup, string ResultFile, string[] RestartArguments);
public sealed record UpdateResult(bool Success, string Message, string Version);

public static class UpdatePackage
{
    public const string Repository = "saymonseo/TypingStats";
    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    public static ReleaseUpdate? SelectRelease(string json, Version current, bool includePreviews = true)
    {
        using var doc = JsonDocument.Parse(json); var updates = new List<ReleaseUpdate>();
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var preview = release.GetProperty("prerelease").GetBoolean(); if (preview && !includePreviews) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.StartsWith('v') ? tag[1..] : tag, out var parsed)) continue;
            var version = Normalize(parsed); if (version <= Normalize(current)) continue;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.StartsWith("TypingStats-win-x64", StringComparison.Ordinal) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                if (asset.TryGetProperty("state", out var state) && state.GetString() != "uploaded") continue;
                var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
                if (digest == null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) || !ValidHash(digest[7..])) continue;
                if (!Uri.TryCreate(asset.GetProperty("browser_download_url").GetString(), UriKind.Absolute, out var uri)) continue;
                var expectedPath = "/" + Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + Uri.EscapeDataString(name);
                if (uri.Scheme != "https" || uri.Host != "github.com" || uri.AbsolutePath != expectedPath || uri.Query.Length != 0) continue;
                var size = asset.GetProperty("size").GetInt64(); if (size <= 0 || size > 64 * 1024 * 1024) continue;
                updates.Add(new ReleaseUpdate(version, tag, name, uri, digest[7..], size, preview)); break;
            }
        }
        return updates.OrderByDescending(u => u.Version).FirstOrDefault();
    }
    private static bool ValidHash(string hash) => hash.Length == 64 && hash.All(Uri.IsHexDigit);
    public static void VerifyHash(string file, string expected)
    {
        if (!ValidHash(expected)) throw new InvalidDataException("У релиза нет корректной SHA-256 суммы.");
        using var stream = File.OpenRead(file);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), Convert.FromHexString(expected))) throw new InvalidDataException("SHA-256 не совпадает. Обновление отменено.");
    }
    public static string SafeRelative(string path)
    {
        var normalized = path.Replace('\\', '/'); var parts = normalized.Split('/');
        if (Path.IsPathRooted(path) || parts.Any(p => p.Length == 0 || p is "." or ".." || p.Contains(':'))) throw new InvalidDataException("Недопустимый путь в пакете обновления.");
        if (parts.Any(p => p.Equals("data", StringComparison.OrdinalIgnoreCase) || p.Equals("backups", StringComparison.OrdinalIgnoreCase) || p.Equals("updates", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Пакет содержит пользовательские данные.");
        var name = parts[^1];
        if (name.Equals("portable.flag", StringComparison.OrdinalIgnoreCase) || name.StartsWith("settings.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("pending-aggregates.json", StringComparison.OrdinalIgnoreCase) || name.Contains(".sqlite", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Обновление не должно содержать настройки или базу.");
        return Path.Combine(parts);
    }
    public static IReadOnlyList<string> Extract(string zipPath, string destination)
    {
        Directory.CreateDirectory(destination); var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(zipPath); if (zip.Entries.Count > 512) throw new InvalidDataException("Слишком много файлов в пакете.");
        var files = new List<(ZipArchiveEntry Entry, string Relative)>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in zip.Entries)
        {
            var path = entry.FullName.Replace('\\', '/'); const string prefix = "TypingStats-win-x64/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Неверная структура Windows-пакета.");
            if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new InvalidDataException("Ссылки в пакете не допускаются.");
            var remainder = path[prefix.Length..];
            if (remainder.Length == 0) continue;
            if (path.EndsWith('/')) { SafeRelative(remainder.TrimEnd('/')); continue; }
            var relative = SafeRelative(remainder); if (!seen.Add(relative)) throw new InvalidDataException("Повторяющийся путь в пакете.");
            total += entry.Length; if (total > 128 * 1024 * 1024) throw new InvalidDataException("Распакованное обновление слишком велико.");
            files.Add((entry, relative));
        }
        foreach (var required in new[] { "TypingStats.exe", "TypingStats.dll", "TypingStats.runtimeconfig.json", "TypingStats.Updater.exe", "TypingStats.Updater.dll", "e_sqlite3.dll" })
            if (!seen.Contains(required)) throw new InvalidDataException("Пакет неполон: " + required);
        foreach (var (entry, relative) in files)
        {
            var target = Path.GetFullPath(Path.Combine(root, relative)); if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Путь выходит за каталог обновления.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); using var input = entry.Open(); using var output = File.Create(target);
            var buffer = new byte[81920]; long count = 0; int n;
            while ((n = input.Read(buffer)) > 0) { count += n; if (count > entry.Length) throw new InvalidDataException("Размер файла не совпадает."); output.Write(buffer, 0, n); }
            if (count != entry.Length) throw new InvalidDataException("Неполный файл обновления.");
        }
        return files.Select(f => f.Relative).ToArray();
    }
}

public sealed class UpdateTransaction
{
    private readonly string source, target, backup;
    private readonly List<(string Relative, bool HadOriginal)> applied = new();
    public UpdateTransaction(string source, string target, string backup)
    {
        this.source = Path.GetFullPath(source); this.target = Path.GetFullPath(target); this.backup = Path.GetFullPath(backup);
        if (this.target == Path.GetPathRoot(this.target) || this.target.Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase)
            || this.target.Equals(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Недопустимый каталог программы.");
        if (!File.Exists(Path.Combine(this.target, "TypingStats.exe"))) throw new InvalidOperationException("В целевой папке не найдена TypingStats.exe.");
    }
    public void Apply()
    {
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories).Select(p => UpdatePackage.SafeRelative(Path.GetRelativePath(source, p))).ToArray();
        try
        {
            foreach (var relative in files)
            {
                var destination = Path.Combine(target, relative); RejectLinks(destination);
                var hadOriginal = File.Exists(destination);
                if (hadOriginal) { var copy = Path.Combine(backup, relative); Directory.CreateDirectory(Path.GetDirectoryName(copy)!); File.Copy(destination, copy, false); }
                applied.Add((relative, hadOriginal)); Replace(Path.Combine(source, relative), destination);
            }
        }
        catch { Rollback(); throw; }
    }
    public void Rollback()
    {
        foreach (var (relative, hadOriginal) in applied.AsEnumerable().Reverse())
        {
            var destination = Path.Combine(target, relative);
            if (hadOriginal) Replace(Path.Combine(backup, relative), destination);
            else if (File.Exists(destination)) File.Delete(destination);
        }
        applied.Clear();
    }
    private void RejectLinks(string destination)
    {
        for (var path = destination; path != null && path.StartsWith(target, StringComparison.OrdinalIgnoreCase); path = Path.GetDirectoryName(path))
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Папка программы содержит ссылку.");
    }
    private static void Replace(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!); var temp = destination + ".update-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, temp); Exception? last = null;
            for (var attempt = 0; attempt < 20; attempt++)
                try { File.Move(temp, destination, true); return; } catch (IOException e) { last = e; Thread.Sleep(150); }
            throw new IOException("Файл программы занят: " + Path.GetFileName(destination), last);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
