using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TypingStats.Core;

if (args.Length != 1) return 2;
var job = JsonSerializer.Deserialize<UpdateJob>(File.ReadAllText(args[0])) ?? throw new InvalidDataException("Нет задания обновления.");
if (!Guid.TryParseExact(job.Id, "N", out _)) throw new InvalidDataException("Неверный идентификатор обновления.");
var source = Path.GetFullPath(job.SourceDirectory); var target = Path.GetFullPath(job.TargetDirectory);
var root = Path.GetDirectoryName(source)!;
if (source == target || !Path.GetFullPath(job.ResultFile).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Неверные пути задания.");
if (Path.GetFileName(job.DataFile) != "stats.sqlite" || !Path.GetFullPath(job.DataBackup).StartsWith(Path.GetDirectoryName(Path.GetFullPath(job.DataFile))! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Неверный путь копии данных.");
var executable = Path.Combine(target, "TypingStats.exe");
try
{
    using var parent = Process.GetProcessById(job.ParentPid);
    if (!Path.GetFullPath(parent.MainModule!.FileName).Equals(executable, StringComparison.OrdinalIgnoreCase)) throw new IOException("Задание относится к другому процессу.");
    if (!parent.WaitForExit(30000)) throw new IOException("Программа ещё работает. Замена файлов отменена.");
}
catch (ArgumentException) { }
catch (Exception e)
{ File.WriteAllText(job.ResultFile, JsonSerializer.Serialize(new UpdateResult(false, e.Message, ""))); return 1; }

var transaction = new UpdateTransaction(source, target, Path.Combine(root, "previous-program-files"));
Process? restarted = null;
try
{
    transaction.Apply();
    using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\TypingStatsUpdateReady-" + job.Id);
    restarted = Start(executable, job.RestartArguments.Concat(["--update-ready", "Local\\TypingStatsUpdateReady-" + job.Id]));
    if (restarted == null || !ready.WaitOne(30000)) throw new IOException("Новая версия не подтвердила запуск. Восстанавливаются прежние файлы.");
    File.WriteAllText(job.ResultFile, JsonSerializer.Serialize(new UpdateResult(true, "Обновление установлено; история и настройки сохранены.", FileVersionInfo.GetVersionInfo(executable).FileVersion ?? "")));
    return 0;
}
catch (Exception e)
{
    var message = e.Message;
    try
    {
        if (restarted is { HasExited: false }) { restarted.Kill(); if (!restarted.WaitForExit(10000)) throw new IOException("Не удалось завершить новую версию."); }
        transaction.Rollback();
        if (restarted != null) RestoreData(job, root);
        File.WriteAllText(job.ResultFile, JsonSerializer.Serialize(new UpdateResult(false, message + " Прежняя версия восстановлена.", "")));
        Start(executable, job.RestartArguments);
    }
    catch (Exception rollback) { File.WriteAllText(job.ResultFile, JsonSerializer.Serialize(new UpdateResult(false, message + " Ошибка восстановления: " + rollback.Message, ""))); }
    return 1;
}
finally { restarted?.Dispose(); }

static Process? Start(string executable, IEnumerable<string> arguments)
{
    var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
    foreach (var argument in arguments) info.ArgumentList.Add(argument); return Process.Start(info);
}
static void RestoreData(UpdateJob job, string root)
{
    SQLitePCL.Batteries_V2.Init();
    if (!File.Exists(job.DataBackup)) throw new IOException("Не найдена копия базы перед обновлением.");
    // Native backup works even when a future application changed the schema version.
    using var old = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = job.DataBackup, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()); old.Open();
    using var live = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = job.DataFile, Pooling = false }.ToString()); live.Open();
    using var failed = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(root, "data-after-failed-update.sqlite"), Pooling = false }.ToString()); failed.Open();
    live.BackupDatabase(failed); old.BackupDatabase(live);
}
