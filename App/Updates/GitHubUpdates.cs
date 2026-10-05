using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using TypingStats.Core;

namespace TypingStats.App.Updates;

internal interface IUpdateTransport : IDisposable
{
    Task<ReleaseUpdate?> Check(bool previews, CancellationToken cancellation);
    Task<string> Download(ReleaseUpdate update, IProgress<int> progress, CancellationToken cancellation, string? dataDirectory = null);
}
internal sealed class GitHubUpdates : IUpdateTransport
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(3) };
    public static Version Current => UpdatePackage.Normalize(Assembly.GetEntryAssembly()!.GetName().Version ?? new Version(0, 5, 0));
    public GitHubUpdates()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TypingStats/" + Current.ToString(3));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }
    public async Task<ReleaseUpdate?> Check(bool previews, CancellationToken cancellation)
    {
        using var response = await http.GetAsync("https://api.github.com/repos/" + UpdatePackage.Repository + "/releases?per_page=30", cancellation);
        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests) throw new IOException("GitHub ограничил частоту запросов. Повторите проверку позже.");
        response.EnsureSuccessStatusCode(); var json = await response.Content.ReadAsStringAsync(cancellation);
        return UpdatePackage.SelectRelease(json, Current, previews);
    }
    public async Task<string> Download(ReleaseUpdate update, IProgress<int> progress, CancellationToken cancellation, string? dataDirectory = null)
    {
        var root = Path.Combine(dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TypingStats"), "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var zip = Path.Combine(root, "package.zip");
        using var response = await http.GetAsync(update.Download, HttpCompletionOption.ResponseHeadersRead, cancellation); response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellation); await using (var output = File.Create(zip))
        {
            var buffer = new byte[81920]; long received = 0; int n;
            while ((n = await input.ReadAsync(buffer, cancellation)) > 0)
            {
                received += n; if (received > update.Size) throw new IOException("Размер скачанного файла превышает размер релиза.");
                await output.WriteAsync(buffer.AsMemory(0, n), cancellation); progress.Report((int)(received * 100 / update.Size));
            }
            if (received != update.Size) throw new IOException("Обновление скачано не полностью.");
        }
        UpdatePackage.VerifyHash(zip, update.Sha256); var staged = Path.Combine(root, "program");
        await Task.Run(() => UpdatePackage.Extract(zip, staged), cancellation); return staged;
    }
    public static async Task Launch(Collector collector, string staged)
    {
        var app = AppContext.BaseDirectory; var sourceHelper = Path.Combine(app, "TypingStats.Updater.exe");
        if (!File.Exists(sourceHelper)) throw new IOException("Не найден помощник обновления. Установите полную сборку 0.3.0 или новее.");
        // Check ordinary write access before closing the running application.
        var probe = Path.Combine(app, ".update-write-test-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(probe, ""); File.Delete(probe);
        var root = Path.GetDirectoryName(staged)!; var helper = Path.Combine(root, "helper"); Directory.CreateDirectory(helper);
        foreach (var file in Directory.GetFiles(app, "*.dll").Concat(Directory.GetFiles(app, "TypingStats.Updater.*"))) File.Copy(file, Path.Combine(helper, Path.GetFileName(file)), true);
        var backupFolder = Path.Combine(collector.Settings.DataDirectory, "backups"); Directory.CreateDirectory(backupFolder);
        var backup = Path.Combine(backupFolder, "before-update-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".sqlite");
        var wasPaused = collector.Paused; await collector.SetPaused(true);
        try
        {
        await collector.ChangeData(s => s.Backup(backup));
        var args = Environment.GetCommandLineArgs().Skip(1).ToList();
        for (var i = args.Count - 1; i >= 0; i--) if (args[i] == "--update-ready") { if (i + 1 < args.Count) args.RemoveAt(i + 1); args.RemoveAt(i); }
        args.RemoveAll(a => a is "--paused" or "--minimized");
        if (wasPaused) args.Add("--paused");
        // Automatic restart must not steal focus. A manually paused collector stays paused.
        args.Add("--minimized");
        var id = Guid.NewGuid().ToString("N"); var result = Path.Combine(root, "result.json");
        var job = new UpdateJob(id, Environment.ProcessId, staged, app, collector.Store.Path, backup, result, args.ToArray());
        var jobPath = Path.Combine(root, "job.json"); File.WriteAllText(jobPath, JsonSerializer.Serialize(job));
        var start = new ProcessStartInfo(Path.Combine(helper, "TypingStats.Updater.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = helper }; start.ArgumentList.Add(jobPath);
        if (Process.Start(start) == null) throw new IOException("Не удалось запустить помощник обновления.");
        }
        catch { await collector.SetPaused(wasPaused); throw; }
    }
    public void Dispose() => http.Dispose();
}
