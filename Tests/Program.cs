using System.Globalization;
using System.Text;
using TypingStats.Core;
using TypingStats.Storage;
using Microsoft.Data.Sqlite;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

var passed = 0;
void Test(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception e) { Console.Error.WriteLine("FAIL " + name + ": " + e.Message); Environment.ExitCode = 1; }
}
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"expected {expected}, got {actual}");
}
var ctx = new InputContext("editor.exe", "ru-RU", "field1");
var time = new DateTimeOffset(2026, 9, 29, 12, 0, 58, TimeSpan.Zero);
Counters Total(TypingEngine e) => Counters.Sum(e.Peek().Select(r => r.Counts));

Test("graphemes: letters, CJK, accent, emoji", () =>
{
    foreach (var s in new[] { "А", "中", "e\u0301", "👩🏽‍🚒", "👨‍👩‍👧‍👦" })
    { var g = new GraphemeStream(); Equal(1, g.Append(s).Delta); g.Reset(); }
});
Test("grapheme across events", () =>
{
    var g = new GraphemeStream(); Equal(1, g.Append("e").Delta); Equal(0, g.Append("\u0301").Delta); Equal(1, g.Append("b").Delta);
});
Test("every Unicode partition has same final total", () =>
{
    foreach (var s in new[] { "e\u0301b", "👩🏽‍🚒x", "中Аé" })
        for (var cut = 1; cut < s.Length; cut++)
        { var g = new GraphemeStream(); var n = g.Append(s.AsSpan(0, cut)).Delta + g.Append(s.AsSpan(cut)).Delta; Equal(new StringInfo(s).LengthInTextElements, n); }
});
Test("one UTF-16 unit per event preserves final graphemes", () =>
{
    foreach (var s in new[] { "👩🏽‍🚒x", "👨‍👩‍👧‍👦", "e\u0301中", "\r\n" })
    { var g = new GraphemeStream(); var count = 0; foreach (var c in s) count += g.Append(new[] { c }).Delta; Equal(new StringInfo(s).LengthInTextElements, count); }
});
Test("gross and linked backspace", () =>
{
    var e = new TypingEngine(); e.Text(ctx, time, 1000, "abc", false); e.Edit(ctx, time, 1100, EditAction.Backspace);
    Equal(3L, Total(e).Gross); Equal(2L, Total(e).Net);
});
Test("paste breaks deletion provenance", () =>
{
    var e = new TypingEngine(); e.Text(ctx, time, 1000, "abc", false); e.Edit(ctx, time, 1100, EditAction.Paste); e.Edit(ctx, time, 1200, EditAction.Backspace);
    Equal(3L, Total(e).Gross); Equal(3L, Total(e).Net); Equal(1L, Total(e).Pastes);
});
Test("navigation, delete, words and undo do not invent size", () =>
{
    foreach (var action in new[] { EditAction.Navigation, EditAction.Delete, EditAction.WordDelete, EditAction.Undo, EditAction.Redo, EditAction.Cut })
    { var e = new TypingEngine(); e.Text(ctx, time, 1000, "abc", false); e.Edit(ctx, time, 1100, action); e.Edit(ctx, time, 1200, EditAction.Backspace); Equal(3L, Total(e).Net); }
});
Test("series cannot go below zero", () =>
{
    var e = new TypingEngine(); e.Text(ctx, time, 1000, "abc", false);
    for (var i = 0; i < 10; i++) e.Edit(ctx, time, 1100 + i, EditAction.Backspace);
    Equal(0L, Total(e).Net); Equal(10L, Total(e).Backspaces);
});
Test("delete in later minute remains signed", () =>
{
    var e = new TypingEngine(); e.Text(ctx, time, 1000, "a", false); e.Edit(ctx, time.AddSeconds(3), 4000, EditAction.Backspace);
    Equal(0L, Total(e).Net); Equal(-1L, e.Peek().Single(r => r.Key.UtcMinute == time.AddSeconds(3).ToUnixTimeSeconds() / 60 * 60).Counts.Net);
});
Test("field change isolates edit series", () =>
{
    var e = new TypingEngine(); e.Text(ctx, time, 1000, "abc", false); e.Edit(ctx with { Field = "field2" }, time, 1200, EditAction.Backspace); Equal(3L, Total(e).Net);
});
Test("composition commit and cancel", () =>
{
    var e = new TypingEngine(); e.Commit(ctx, time, 1000, "日本"); e.Commit(ctx, time, 1200, "");
    Equal(2L, Total(e).Observed); Equal(0L, Total(e).Estimated); Equal(1L, Total(e).Cancels);
});
Test("same composition repeated is counted twice", () =>
{
    var e = new TypingEngine(); e.CommitCount(ctx, time, 1000, 2, 0); e.CommitCount(ctx, time, 1200, 2, 0); Equal(4L, Total(e).Gross);
});
Test("idle duration measured in monotonic milliseconds", () =>
{
    var e = new TypingEngine(); e.Text(ctx, time, 1000, "a", false); e.Text(ctx, time.AddSeconds(2), 3000, "b", false); e.Advance(time.AddSeconds(10), 11000);
    Equal(7000L, Total(e).ActiveMs);
});
Test("pause stops trailing active interval", () =>
{
    var e = new TypingEngine(); e.Text(ctx, time, 1000, "a", false); e.Stop(time.AddSeconds(1), 2000, "pause"); e.Advance(time.AddHours(1), 3601000); Equal(1000L, Total(e).ActiveMs);
});
Test("clock jump cannot create phantom hours", () =>
{
    var e = new TypingEngine(); e.Text(ctx, time, 1000, "a", false); e.Advance(time.AddHours(5), 2000); Equal(1000L, Total(e).ActiveMs);
});
Test("primary/repeat/injected counters independent of text", () =>
{
    var e = new TypingEngine(); e.Key(ctx, time, 1000, false); e.Key(ctx, time, 1100, true); e.Key(ctx, time, 1200, false, true);
    Equal(1L, Total(e).PrimaryKeys); Equal(1L, Total(e).Repeats); Equal(1L, Total(e).Injected); Equal(0L, Total(e).Gross);
});
Test("drain preserves stable batch id for retry", () =>
{
    var e = new TypingEngine(); e.Text(ctx, time, 1000, "abc", false); var b = e.Drain(); Equal(3L, Counters.Sum(b.Rows.Select(r => r.Counts)).Gross); Equal(0L, Total(e).Gross);
});
Test("loss breaks edit provenance", () =>
{
    var e = new TypingEngine(); e.Text(ctx, time, 1000, "abc", false); e.Lost(ctx, time, 4); e.Edit(ctx, time, 1100, EditAction.Backspace); Equal(3L, Total(e).Net); Equal(4L, Total(e).Lost);
});
Test("SQLite idempotence, rollups, backup, restore, privacy, CSV", () =>
{
    var folder = Path.Combine(Path.GetTempPath(), "TypingStats-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
    using var store = new StatsStore(Path.Combine(folder, "stats.sqlite"));
    var e = new TypingEngine(); e.Text(ctx, time, 1000, "secret-not-to-persist", false); var batch = e.Drain();
    store.Save(batch); store.Save(batch);
    var date = time.ToLocalTime().ToString("yyyy-MM-dd");
    foreach (var res in new[] { 0, 1, 2 }) Equal(21L, Counters.Sum(store.Read(date, date, res).Select(r => r.Counts)).Gross);
    store.Backup(Path.Combine(folder, "backup.sqlite")); store.Clear(); Equal(0, store.Read(date, date).Count);
    store.Restore(Path.Combine(folder, "backup.sqlite")); Equal(21L, Counters.Sum(store.Read(date, date).Select(r => r.Counts)).Gross);
    StatsStore.Export(store.Read(date, date), Path.Combine(folder, "export.csv"));
    var csv = File.ReadAllText(Path.Combine(folder, "export.csv")); Equal(false, csv.Contains("secret-not-to-persist"));
    store.Backup(Path.Combine(folder, "inspect.sqlite")); var bytes = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(folder, "inspect.sqlite"))); Equal(false, bytes.Contains("secret-not-to-persist"));
    store.SetCategory("editor.exe", "Код"); Equal("Код", store.Categories()["editor.exe"]);
    // Test artifacts intentionally preserved in a named temp folder for inspection.
});
Test("all-key policy independent of text context", () =>
{
    Equal(true, KeyIdentity.CountPress(TrackingMode.Keys, false, false, true, false));
    Equal(true, KeyIdentity.CountPress(TrackingMode.Both, false, false, true, false));
    Equal(false, KeyIdentity.CountPress(TrackingMode.Text, false, false, true, false));
    Equal(false, KeyIdentity.CountPress(TrackingMode.Keys, true, false, false, false));
    Equal(false, KeyIdentity.CountPress(TrackingMode.Keys, false, true, true, false));
    Equal(true, KeyIdentity.CountPress(TrackingMode.Keys, false, true, true, true));
});
Test("left/right Ctrl and main/numpad Enter are separate", () =>
{
    Equal("sc:001D", KeyIdentity.Code(0x1d, false, 0xa2)); Equal("sc:E01D", KeyIdentity.Code(0x1d, true, 0xa3));
    Equal("sc:001C", KeyIdentity.Code(0x1c, false, 0x0d)); Equal("sc:E01C", KeyIdentity.Code(0x1c, true, 0x0d));
    Equal("A / Ф", KeyIdentity.Label(0x1e, false, 0x41));
});
Test("Unicode software packets never persist character-valued scan codes", () =>
{ Equal("vk:E7", KeyIdentity.Code(0x042f, false, 0xe7)); Equal(KeyIdentity.Label(65, false, 0xe7), KeyIdentity.Label(66, false, 0xe7)); });
Test("key frequency includes modifiers and repeat but creates no text", () =>
{
    var e = new TypingEngine();
    e.KeyPress(ctx, time, 1000, "sc:0011", "W / Ц"); e.KeyPress(ctx, time.AddSeconds(1), 2000, "sc:0011", "W / Ц", true);
    e.KeyPress(ctx, time.AddSeconds(2), 3000, "sc:002A", "Left Shift"); e.Stop(time.AddSeconds(3), 4000, "test");
    Equal(3L, Total(e).KeyPresses); Equal(0L, Total(e).Gross); Equal(3000L, Total(e).KeyActiveMs);
    Equal(2L, e.PeekKeys().Single(k => k.Code == "sc:0011").Presses);
    Equal(1L, e.PeekKeys().Single(k => k.Code == "sc:0011").Repeats);
    var batch = e.Drain(); Equal(3L, batch.Sessions.Single().KeyPresses); Equal(3000L, batch.Sessions.Single().KeyActiveMs);
});
Test("key active intervals do not overlap across applications", () =>
{
    var e = new TypingEngine(); e.KeyPress(ctx, time, 1000, "sc:0011", "W");
    e.SetContext(ctx with { App = "game.exe" }, time.AddSeconds(1), 2000); e.Advance(time.AddSeconds(10), 11000);
    Equal(1000L, Total(e).KeyActiveMs);
});
Test("key daily storage retries, backup, CSV and clear", () =>
{
    var folder = Path.Combine(Path.GetTempPath(), "TypingStats-keys-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
    using var store = new StatsStore(Path.Combine(folder, "stats.sqlite")); var e = new TypingEngine();
    e.KeyPress(ctx, time, 1000, "sc:0011", "W / Ц"); e.KeyPress(ctx, time, 1100, "sc:0011", "W / Ц", true); e.Stop(time, 1200, "test");
    var batch = e.Drain(); store.Save(batch); store.Save(batch); var date = time.ToLocalTime().ToString("yyyy-MM-dd");
    Equal(2L, store.ReadKeys(date, date).Single().Presses); Equal(1L, store.ReadKeys(date, date).Single().Repeats);
    Equal(2L, store.Sessions().Single().KeyPresses);
    store.Backup(Path.Combine(folder, "backup.sqlite")); store.Clear(); Equal(0, store.ReadKeys(date, date).Count);
    store.Restore(Path.Combine(folder, "backup.sqlite")); Equal(2L, store.ReadKeys(date, date).Single().Presses);
    StatsStore.ExportKeys(store.ReadKeys(date, date), Path.Combine(folder, "keys.csv")); Equal(true, File.ReadAllText(Path.Combine(folder, "keys.csv")).Contains("sc:0011"));
});
Test("schema 1 migration preserves text statistics and creates backup", () =>
{
    var folder = Path.Combine(Path.GetTempPath(), "TypingStats-migration-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); var path = Path.Combine(folder, "stats.sqlite");
    using (var store = new StatsStore(path)) { var e = new TypingEngine(); e.Text(ctx, time, 1000, "abc", false); store.Save(e.Drain()); }
    using (var db = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
    {
        db.Open(); using var c = db.CreateCommand(); c.CommandText = "ALTER TABLE metrics DROP COLUMN KeyPresses; ALTER TABLE metrics DROP COLUMN KeyActiveMs; ALTER TABLE sessions DROP COLUMN KeyPresses; ALTER TABLE sessions DROP COLUMN KeyActiveMs; DROP TABLE key_counts; PRAGMA user_version=1;"; c.ExecuteNonQuery();
    }
    using (var upgraded = new StatsStore(path))
    {
        var date = time.ToLocalTime().ToString("yyyy-MM-dd"); Equal(3L, Counters.Sum(upgraded.Read(date, date).Select(r => r.Counts)).Gross);
        Equal(0, upgraded.ReadKeys(date, date).Count); Equal(1, Directory.GetFiles(folder, "before-schema-2-*.sqlite").Length);
    }
});
KeyMetricRow Stat(string day, string app, string code, string label, long presses, long repeats = 0) => new(new BucketKey(0, day, -1, 0, "", app, "test"), code, label, presses, repeats, 0);
var statRows = new[] { Stat("2026-10-03", "game.exe", "sc:0011", "W / Ц", 7, 2), Stat("2026-10-03", "game.exe", "sc:001D", "Left Ctrl", 2),
    Stat("2026-10-04", "game.exe", "sc:0039", "Space", 5), Stat("2026-10-04", "word.exe", "sc:001E", "A / Ф", 4),
    Stat("2026-10-05", "word.exe", "sc:0011", "W / Ц", 2), Stat("2026-10-05", "game.exe", "sc:0011", "W / Ц", 3), Stat("2026-10-05", "word.exe", "sc:E05B", "Left Win", 1) };
var range = new StatisticsFilter(new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 5));
Test("statistics inclusive dates, daily totals and key breakdown", () =>
{
    var report = KeyStatistics.Build(statRows, range); Equal(24L, report.Presses); Equal(5, report.DistinctKeys); Equal(3, report.DataDays); Equal(2, report.Applications);
    Equal(24L, report.Days.Sum(d => d.Presses)); Equal(12L, report.Breakdown().Single(k => k.Code == "sc:0011").Presses);
    Equal(9L, report.Breakdown(new DateOnly(2026,10,4)).Sum(k => k.Presses));
});
Test("statistics application, category, Latin/Cyrillic exact letter filters", () =>
{
    Equal(17L, KeyStatistics.Build(statRows, range with { Application = "GAME.EXE" }).Presses);
    Equal(3L, KeyStatistics.Build(statRows, range with { Group = KeyGroup.Modifiers }).Presses);
    Equal(12L, KeyStatistics.Build(statRows, range with { Search = "w" }).Presses); // W must not also match Win.
    Equal(12L, KeyStatistics.Build(statRows, range with { Search = "ц" }).Presses);
    Equal(5L, KeyStatistics.Build(statRows, range with { Search = "пробел" }).Presses);
    Equal(10L, KeyStatistics.Build(statRows, range with { Application = "game.exe", Group = KeyGroup.Letters }).Presses);
});
Test("empty days and no matching records remain explicit", () =>
{
    var r = KeyStatistics.Build(statRows, range with { From = new(2026,10,2), To = new(2026,10,6), IncludeEmptyDays = true });
    Equal(5, r.Days.Count); Equal(3, r.DataDays); Equal(24L, r.Days.Sum(d => d.Presses)); Equal(0L, r.Days.First().Presses);
    Equal(0L, KeyStatistics.Build(statRows, range with { Search = "missing-key" }).Presses);
    try { KeyStatistics.Build(statRows, range with { From = new(2026,10,6) }); throw new Exception("Invalid dates accepted"); } catch (ArgumentException) { }
});
Test("CSV period scope differs from selected day and respects filters", () =>
{
    var r = KeyStatistics.Build(statRows, range with { Application = "game.exe" });
    var folder = Path.Combine(Path.GetTempPath(), "TypingStats-csv-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
    StatisticsCsv.Write(r, Path.Combine(folder, "period.csv"), StatisticsExport.Detailed);
    StatisticsCsv.Write(r, Path.Combine(folder, "day.csv"), StatisticsExport.Detailed, new DateOnly(2026,10,4));
    var all = File.ReadAllText(Path.Combine(folder, "period.csv")); var day = File.ReadAllText(Path.Combine(folder, "day.csv"));
    Equal(true, all.Contains("2026-10-03")); Equal(false, all.Contains("word.exe")); Equal(false, day.Contains("2026-10-03")); Equal(true, day.Contains("Space"));
    StatisticsCsv.Write(r, Path.Combine(folder,"daily.csv"), StatisticsExport.Daily); Equal(4, File.ReadAllLines(Path.Combine(folder,"daily.csv")).Length);
    StatisticsCsv.Write(r, Path.Combine(folder,"keys.csv"), StatisticsExport.Keys); Equal(true, File.ReadAllText(Path.Combine(folder,"keys.csv")).Contains("58.82"));
});
Test("CSV quotes data, protects formulas and merges pending day rows", () =>
{
    var rows = new[] { Stat("2026-10-03", " =HYPERLINK(\"x\")", "sc:001E", "=", 1), Stat("2026-10-03", " =HYPERLINK(\"x\")", "sc:001E", "=", 2) };
    var r = KeyStatistics.Build(rows, range); var text = StatisticsCsv.Render(r, r.Rows, StatisticsExport.Detailed);
    Equal(3L, r.Presses); Equal(true, text.Contains("' =HYPERLINK")); Equal(true, text.Contains("\"'=\"")); Equal(2, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
});
string Releases(params object[] releases) => JsonSerializer.Serialize(releases);
object Release(string tag, bool preview = false, bool draft = false, string? hash = null, string? host = null) => new { tag_name = tag, draft, prerelease = preview,
    assets = new[] { new { name = "TypingStats-win-x64-" + tag + ".zip", state = "uploaded", size = 1234L, digest = "sha256:" + (hash ?? new string('a',64)), browser_download_url = "https://" + (host ?? "github.com") + "/saymonseo/TypingStats/releases/download/" + tag + "/TypingStats-win-x64-" + tag + ".zip" } } };
Test("GitHub update selection compares numeric versions and preview policy", () =>
{
    var json = Releases(Release("v0.9.0"), Release("v0.10.0",true), Release("v9.0.0",draft:true));
    Equal(new Version(0,10,0,0), UpdatePackage.SelectRelease(json,new Version(0,3,0))!.Version);
    Equal(new Version(0,9,0,0), UpdatePackage.SelectRelease(json,new Version(0,3,0),false)!.Version);
    Equal(null, UpdatePackage.SelectRelease(Releases(Release("v0.3.0")),new Version(0,3,0,0)));
});
Test("GitHub update requires expected owner HTTPS URL and SHA256", () =>
{
    Equal(null, UpdatePackage.SelectRelease(Releases(Release("v0.4.0",host:"evil.example")),new Version(0,3,0)));
    Equal(null, UpdatePackage.SelectRelease(Releases(Release("v0.4.0",hash:"bad")),new Version(0,3,0)));
    var file = Path.Combine(Path.GetTempPath(),"TypingStats-hash-" + Guid.NewGuid().ToString("N")); File.WriteAllText(file,"known-content");
    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))); UpdatePackage.VerifyHash(file,hash);
    try { UpdatePackage.VerifyHash(file,new string('0',64)); throw new Exception("Corrupt update accepted"); } catch(InvalidDataException) { }
});
Test("package rejects traversal, private data and absolute paths", () =>
{
    foreach(var name in new[] { "../bad.exe", "C:/bad.exe", "data/stats.sqlite", "portable.flag", "settings.json", "backups/history.sqlite" })
        try { UpdatePackage.SafeRelative(name); throw new Exception("Unsafe path accepted: " + name); } catch(InvalidDataException) { }
    Equal(Path.Combine("docs","readme.md"),UpdatePackage.SafeRelative("docs/readme.md"));
});
Test("ZIP validates all entries before extraction", () =>
{
    var folder=Path.Combine(Path.GetTempPath(),"TypingStats-package-" + Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
    var zip=Path.Combine(folder,"package.zip");
    using(var z=ZipFile.Open(zip,ZipArchiveMode.Create)) foreach(var name in new[]{"TypingStats.exe","TypingStats.dll","TypingStats.runtimeconfig.json","TypingStats.Updater.exe","TypingStats.Updater.dll","e_sqlite3.dll"})
        { using var w=new StreamWriter(z.CreateEntry("TypingStats-win-x64/"+name).Open());w.Write("fixture"); }
    Equal(6,UpdatePackage.Extract(zip,Path.Combine(folder,"valid")).Count);
    using(var z=ZipFile.Open(zip,ZipArchiveMode.Update)){using var w=new StreamWriter(z.CreateEntry("TypingStats-win-x64/../outside.txt").Open());w.Write("bad");}
    try{UpdatePackage.Extract(zip,Path.Combine(folder,"invalid"));throw new Exception("Traversal accepted");}catch(InvalidDataException){}
    Equal(false,File.Exists(Path.Combine(folder,"outside.txt")));
});
Test("program transaction preserves portable data and can rollback", () =>
{
    var root=Path.Combine(Path.GetTempPath(),"TypingStats-update-"+Guid.NewGuid().ToString("N"));var src=Path.Combine(root,"new");var dst=Path.Combine(root,"app");
    Directory.CreateDirectory(src);Directory.CreateDirectory(Path.Combine(dst,"data"));File.WriteAllText(Path.Combine(src,"TypingStats.exe"),"new");File.WriteAllText(Path.Combine(src,"extra.dll"),"extra");
    File.WriteAllText(Path.Combine(dst,"TypingStats.exe"),"old");File.WriteAllText(Path.Combine(dst,"portable.flag"),"");File.WriteAllText(Path.Combine(dst,"data","stats.sqlite"),"history");
    var tx=new UpdateTransaction(src,dst,Path.Combine(root,"rollback"));tx.Apply();Equal("new",File.ReadAllText(Path.Combine(dst,"TypingStats.exe")));Equal("history",File.ReadAllText(Path.Combine(dst,"data","stats.sqlite")));
    tx.Rollback();Equal("old",File.ReadAllText(Path.Combine(dst,"TypingStats.exe")));Equal(false,File.Exists(Path.Combine(dst,"extra.dll")));Equal(true,File.Exists(Path.Combine(dst,"portable.flag")));
});
Test("application identity distinguishes same executable in separate images",()=>
{
    var folder=Path.Combine(Path.GetTempPath(),"TypingStats-appnames-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
    using var store=new StatsStore(Path.Combine(folder,"stats.sqlite"));
    var first=store.RegisterApplication("host.exe","First App",new string('a',64));var second=store.RegisterApplication("host.exe","Second App",new string('b',64));
    Equal("host.exe",first.Id);Equal(false,first.Id==second.Id);Equal(second.Id,store.RegisterApplication("host.exe","Second App",new string('b',64)).Id);
    store.RegisterApplication("host.exe","host.exe","");Equal("First App",store.ApplicationNames()[first.Id].Name);
    var report=KeyStatistics.Build(new[]{Stat("2026-10-03",first.Id,"sc:0011","W",4),Stat("2026-10-03",second.Id,"sc:0011","W",6)},range with{Application=second.Id});Equal(6L,report.Presses);
    var csv=StatisticsCsv.Render(report,report.Rows,StatisticsExport.Detailed,applications:store.ApplicationNames());Equal(true,csv.Contains("Second App"));Equal(false,csv.Contains("First App"));
});
Console.WriteLine($"FINAL passed {passed} tests; exit code {Environment.ExitCode}");
