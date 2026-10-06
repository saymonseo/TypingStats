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
        Equal(0, upgraded.ReadKeys(date, date).Count); Equal(1, Directory.GetFiles(folder, "before-schema-3-*.sqlite").Length);
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
Test("mouse down messages separate all five buttons and ignore up, move, wheel", () =>
{
    Equal<MouseButton?>(MouseButton.Left, MouseStatistics.FromMessage(0x201, 0)); Equal<MouseButton?>(MouseButton.Right, MouseStatistics.FromMessage(0x204, 0));
    Equal<MouseButton?>(MouseButton.Middle, MouseStatistics.FromMessage(0x207, 0)); Equal<MouseButton?>(MouseButton.X1, MouseStatistics.FromMessage(0x20b, 1u << 16));
    Equal<MouseButton?>(MouseButton.X2, MouseStatistics.FromMessage(0x20b, 2u << 16));
    foreach (var message in new[] { 0x200, 0x202, 0x205, 0x208, 0x20c, 0x20a, 0x20e, 0x203 }) Equal<MouseButton?>(null, MouseStatistics.FromMessage(message, 1u << 16));
    Equal<MouseButton?>(null, MouseStatistics.FromMessage(0x20b, 3u << 16));
});
Test("mouse policy: independent enable and injected switches", () =>
{
    Equal(true, MouseStatistics.CountPress(true, false, false)); Equal(false, MouseStatistics.CountPress(false, false, true));
    Equal(false, MouseStatistics.CountPress(true, true, false)); Equal(true, MouseStatistics.CountPress(true, true, true));
});
Test("mouse aggregations never add keyboard or text metrics and survive drain", () =>
{
    var e = new TypingEngine(); foreach (var b in Enum.GetValues<MouseButton>()) e.MousePress("game.exe", time, b);
    e.MousePress("game.exe", time, MouseButton.Left); e.MousePress("game.exe", time, MouseButton.Left);
    e.MousePress("browser.exe", time, MouseButton.X2, true);
    Equal(8L, e.PeekMouse().Sum(m => m.Presses)); Equal(3L, e.PeekMouse().Single(m => m.Button == MouseButton.Left).Presses);
    Equal(0, e.Peek().Count); Equal(0, e.PeekKeys().Count);
    var batch = e.Drain(); Equal(8L, batch.Mouse!.Sum(m => m.Presses)); Equal(1L, batch.Mouse!.Sum(m => m.Injected)); Equal(0, batch.Sessions.Count); Equal(0, e.PeekMouse().Count);
    var restored = JsonSerializer.Deserialize<FlushBatch>(JsonSerializer.Serialize(batch))!; Equal(8L, restored.Mouse!.Sum(m => m.Presses));
    var legacy = JsonSerializer.Deserialize<FlushBatch>("{\"Id\":\"old\",\"Rows\":[],\"Sessions\":[],\"Keys\":[]}")!; Equal(null, legacy.Mouse);
});
Test("mouse dates use local midnight and separate applications", () =>
{
    var e = new TypingEngine(); var local = new DateTimeOffset(new DateTime(2026, 10, 3, 23, 59, 59, DateTimeKind.Local));
    e.MousePress("one.exe", local, MouseButton.Left); e.MousePress("two.exe", local.AddSeconds(2), MouseButton.Left);
    Equal("2026-10-03", e.PeekMouse()[0].LocalDate); Equal("2026-10-04", e.PeekMouse()[1].LocalDate); Equal(2, e.PeekMouse().Select(r => r.App).Distinct().Count());
});
var mouseFixture = new[] { new MouseMetricRow("2026-10-03", "game.exe", MouseButton.Left, 10, 0), new MouseMetricRow("2026-10-03", "game.exe", MouseButton.Right, 4, 0),
    new MouseMetricRow("2026-10-04", "browser.exe", MouseButton.Middle, 3, 1), new MouseMetricRow("2026-10-05", "game.exe", MouseButton.X1, 2, 0) };
var mouseRange = new MouseStatisticsFilter(new(2026, 10, 3), new(2026, 10, 5));
Test("mouse report filters, day totals, button shares, zeros", () =>
{
    var r = MouseStatistics.Build(mouseFixture, mouseRange); Equal(19L, r.Presses); Equal(4, r.UsedButtons); Equal(3, r.DataDays); Equal(2, r.Applications); Equal(5, r.Breakdown().Count);
    Equal(19L, r.Days.Sum(d => d.Presses)); Equal(14L, r.Breakdown(new(2026, 10, 3)).Sum(b => b.Presses)); Equal(0L, r.Breakdown().Single(b => b.Button == MouseButton.X2).Presses);
    Equal(16L, MouseStatistics.Build(mouseFixture, mouseRange with { Application = "GAME.EXE" }).Presses);
    Equal(3L, MouseStatistics.Build(mouseFixture, mouseRange with { Button = MouseButton.Middle }).Presses);
    var empty = MouseStatistics.Build(mouseFixture, mouseRange with { From = new(2026, 10, 2), IncludeEmptyDays = true }); Equal(4, empty.Days.Count); Equal(3, empty.DataDays);
    try { MouseStatistics.Build(mouseFixture, mouseRange with { From = new(2026, 10, 6) }); throw new Exception("Invalid period accepted"); } catch (ArgumentException) { }
});
Test("mouse CSV honors filters, dates, app names and safe cells", () =>
{
    var r = MouseStatistics.Build(mouseFixture, mouseRange with { Application = "game.exe" });
    var names = new Dictionary<string, ApplicationIdentity> { ["game.exe"] = new("game.exe", "=Example,Game", "game.exe", "") };
    var detail = MouseStatisticsCsv.Render(r, MouseStatisticsExport.Detailed, applications: names); Equal(false, detail.Contains("browser.exe")); Equal(true, detail.Contains("\"'=Example,Game\""));
    var day = MouseStatisticsCsv.Render(r, MouseStatisticsExport.Detailed, new(2026, 10, 3)); Equal(false, day.Contains("2026-10-05"));
    Equal(true, MouseStatisticsCsv.Render(r, MouseStatisticsExport.Buttons).Contains("62.5"));
    Equal(true, MouseStatisticsCsv.Render(r, MouseStatisticsExport.Daily).Contains("14"));
    var folder = Path.Combine(Path.GetTempPath(), "TypingStats-mouse-csv-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); var path = Path.Combine(folder, "mouse.csv");
    MouseStatisticsCsv.Write(r, path, MouseStatisticsExport.Detailed); Equal(true, File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }));
    var original = File.ReadAllText(path); try { MouseStatisticsCsv.Write(r, path, MouseStatisticsExport.Detailed, new(2026, 10, 6)); } catch (ArgumentException) { }
    Equal(original, File.ReadAllText(path));
});
Test("mouse SQLite atomic replay, backup, clear, restore and privacy", () =>
{
    var folder = Path.Combine(Path.GetTempPath(), "TypingStats-mouse-db-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
    using var store = new StatsStore(Path.Combine(folder, "stats.sqlite")); var e = new TypingEngine(); e.MousePress("game.exe", time, MouseButton.Left); e.MousePress("game.exe", time, MouseButton.X2, true);
    var batch = e.Drain(); store.Save(batch); store.Save(batch); var date = time.ToLocalTime().ToString("yyyy-MM-dd");
    Equal(2L, store.ReadMouse(date, date).Sum(r => r.Presses)); Equal(0, store.Read(date, date).Count); Equal(0, store.ReadKeys(date, date).Count);
    var backup = Path.Combine(folder, "backup.sqlite"); store.Backup(backup); store.Clear(); Equal(0, store.ReadMouse(date, date).Count);
    store.Restore(backup); Equal(2L, store.ReadMouse(date, date).Sum(r => r.Presses)); Equal(1L, store.ReadMouse(date, date).Sum(r => r.Injected));
    using var db = new SqliteConnection("Data Source=" + backup); db.Open(); using var c = db.CreateCommand(); c.CommandText = "PRAGMA table_info(mouse_counts)";
    using var rd = c.ExecuteReader(); var columns = new List<string>(); while (rd.Read()) columns.Add(rd.GetString(1));
    Equal("local_date,app,button,presses,injected", string.Join(",", columns));
});
Test("schema 2 upgrades automatically preserving keyboard, sessions, names and backup", () =>
{
    var folder = Path.Combine(Path.GetTempPath(), "TypingStats-mouse-migration-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); var path = Path.Combine(folder, "stats.sqlite");
    using (var s = new StatsStore(path))
    {
        var e = new TypingEngine(); e.Text(ctx, time, 1000, "old", false); e.KeyPress(ctx, time, 1000, "sc:0011", "W"); e.Stop(time, 2000, "test"); s.Save(e.Drain()); s.RegisterApplication("editor.exe", "Editor", "");
    }
    using (var db = new SqliteConnection("Data Source=" + path)) { db.Open(); using var c = db.CreateCommand(); c.CommandText = "DROP TABLE mouse_counts; PRAGMA user_version=2"; c.ExecuteNonQuery(); }
    using (var s = new StatsStore(path))
    {
        Equal(3L, Counters.Sum(s.Read("2000-01-01", "9999-12-31", 2).Select(r => r.Counts)).Gross); Equal(1L, s.ReadKeys("2000-01-01", "9999-12-31").Sum(r => r.Presses)); Equal(1, s.Sessions().Count);
        Equal("Editor", s.ApplicationNames()["editor.exe"].Name); Equal(0, s.ReadMouse("2000-01-01", "9999-12-31").Count);
    }
    Equal(1, Directory.GetFiles(folder, "before-schema-3-*.sqlite").Length);
});
Test("automatic update schedule respects startup, six hours, disable and clock rollback", () =>
{
    var startup = new DateTimeOffset(2026,10,5,12,0,0,TimeSpan.Zero);
    Equal(false, AutomaticUpdatePolicy.CheckDue(true,startup.AddSeconds(29),startup,null));
    Equal(true, AutomaticUpdatePolicy.CheckDue(true,startup.AddSeconds(30),startup,null));
    Equal(false, AutomaticUpdatePolicy.CheckDue(false,startup.AddDays(1),startup,null));
    Equal(false, AutomaticUpdatePolicy.CheckDue(true,startup.AddHours(5),startup,startup));
    Equal(true, AutomaticUpdatePolicy.CheckDue(true,startup.AddHours(6),startup,startup));
    Equal(true, AutomaticUpdatePolicy.CheckDue(true,startup.AddHours(1),startup,startup.AddHours(2)));
});
Test("automatic install requires hidden UI, no dialog, unlocked idle and healthy storage", () =>
{
    Equal(true, AutomaticUpdatePolicy.CanInstall(true,false,false,false,60000,true));
    Equal(false, AutomaticUpdatePolicy.CanInstall(true,false,false,false,59999,true));
    Equal(false, AutomaticUpdatePolicy.CanInstall(true,true,false,false,60000,true));
    Equal(false, AutomaticUpdatePolicy.CanInstall(true,false,true,false,60000,true));
    Equal(false, AutomaticUpdatePolicy.CanInstall(true,false,false,true,60000,true));
    Equal(false, AutomaticUpdatePolicy.CanInstall(true,false,false,false,60000,false));
    Equal(false, AutomaticUpdatePolicy.CanInstall(false,false,false,false,60000,true));
    Equal(false, AutomaticUpdatePolicy.CanInstall(true,false,false,false,-1,true));
});
Test("update rollback detection rejects endless retries but clears successful attempt", () =>
{
    Equal(true, AutomaticUpdatePolicy.ShouldSuppress("0.6.0",new Version(0,5,0)));
    Equal(false, AutomaticUpdatePolicy.ShouldSuppress("0.5.0",new Version(0,5,0,0)));
    Equal(false, AutomaticUpdatePolicy.ShouldSuppress("invalid",new Version(0,5,0)));
});
Test("theme choices follow system only when selected and both palettes have readable text", () =>
{
    Equal(true, ThemePalette.IsDark(AppTheme.System,true)); Equal(false, ThemePalette.IsDark(AppTheme.System,false));
    Equal(false, ThemePalette.IsDark(AppTheme.Light,true)); Equal(true, ThemePalette.IsDark(AppTheme.Dark,false));
    double Luminance(System.Drawing.Color c)
    {
        double Linear(byte n) {var v=n/255.0;return v<=0.04045?v/12.92:Math.Pow((v+0.055)/1.055,2.4);}
        return Linear(c.R)*.2126+Linear(c.G)*.7152+Linear(c.B)*.0722;
    }
    double Contrast(System.Drawing.Color a,System.Drawing.Color b){var x=Luminance(a);var y=Luminance(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
    foreach(var p in new[]{ThemePalette.Light,ThemePalette.Dark})
    {Equal(true,Contrast(p.Ink,p.Card)>=4.5);Equal(true,Contrast(p.Muted,p.Card)>=4.5);Equal(true,Contrast(p.Muted,p.Page)>=4.5);}
});
Test("Windows startup command quotes spaced paths and explicitly uses tray and data directory", () =>
{
    Equal("\"C:\\Program Files\\Typing Stats\\TypingStats.exe\" --minimized --data-dir \"C:\\User data\\TypingStats\"",
        StartupCommand.Build(@"C:\Program Files\Typing Stats\TypingStats.exe", @"C:\User data\TypingStats"));
});
Test("Windows argument quoting escapes embedded quotes and trailing backslashes", () =>
{
    Equal("\"C:\\\\\"", StartupCommand.Quote(@"C:\"));
    Equal("\"a\\\"b\"", StartupCommand.Quote("a\"b"));
    Equal("\"C:\\Typing Stats\"", StartupCommand.Quote(@"C:\Typing Stats"));
});
Test("Windows Run command rejects unusable arguments and overlong registrations", () =>
{
    foreach(var value in new[]{""," ","a\nb","a\0b"})
        try{StartupCommand.Quote(value);throw new Exception("Invalid argument accepted");}catch(ArgumentException){}
    try{StartupCommand.Build("C:\\"+new string('x',260)+".exe",@"C:\data");throw new Exception("Overlong Run command accepted");}catch(ArgumentException){}
});
Console.WriteLine($"FINAL passed {passed} tests; exit code {Environment.ExitCode}");
