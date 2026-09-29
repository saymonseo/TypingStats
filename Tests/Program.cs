using System.Globalization;
using System.Text;
using TypingStats.Core;
using TypingStats.Storage;
using Microsoft.Data.Sqlite;

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
Console.WriteLine($"TOTAL passed {passed} tests; exit code {Environment.ExitCode}");
