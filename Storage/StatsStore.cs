using System.Globalization;
using System.Reflection;
using System.Text;
using Microsoft.Data.Sqlite;
using TypingStats.Core;

namespace TypingStats.Storage;

public sealed class StatsStore : IDisposable
{
    private readonly SqliteConnection db;
    private readonly object gate = new();
    private static readonly PropertyInfo[] Fields = typeof(Counters).GetProperties().Where(p => p.CanWrite).ToArray();
    public string Path { get; }
    public StatsStore(string path)
    {
        SQLitePCL.Batteries_V2.Init(); Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false, DefaultTimeout = 2 }.ToString());
        db.Open();
        Exec("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;");
        using var check = db.CreateCommand(); check.CommandText = "PRAGMA quick_check";
        if ((string?)check.ExecuteScalar() != "ok") throw new InvalidDataException("Проверка базы не пройдена. Исходный файл сохранён.");
        using var version = db.CreateCommand(); version.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(version.ExecuteScalar()) > 1) throw new InvalidDataException("База создана более новой версией TypingStats.");
        var columns = string.Join(",", Fields.Select(p => $"{p.Name} INTEGER NOT NULL DEFAULT 0"));
        Exec($"""
            CREATE TABLE IF NOT EXISTS metrics(
              bucket INTEGER NOT NULL, resolution INTEGER NOT NULL, local_date TEXT NOT NULL,
              hour INTEGER NOT NULL, offset INTEGER NOT NULL, zone TEXT NOT NULL, app TEXT NOT NULL,
              profile TEXT NOT NULL, {columns},
              PRIMARY KEY(bucket,resolution,local_date,hour,offset,zone,app,profile));
            CREATE INDEX IF NOT EXISTS ix_metrics_date ON metrics(resolution,local_date);
            CREATE TABLE IF NOT EXISTS batches(id TEXT PRIMARY KEY, committed INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS sessions(id TEXT PRIMARY KEY, started TEXT NOT NULL, ended TEXT NOT NULL,
              gross INTEGER NOT NULL, active INTEGER NOT NULL, reason TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS applications(app TEXT PRIMARY KEY, category TEXT NOT NULL DEFAULT 'Прочее');
            PRAGMA user_version=1;
            """);
    }
    private void Exec(string sql)
    { using var c = db.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
    public void Save(FlushBatch batch)
    {
        if (batch.Rows.Count == 0 && batch.Sessions.Count == 0) return;
        lock (gate)
        {
            using var tx = db.BeginTransaction();
            using var marker = db.CreateCommand(); marker.Transaction = tx;
            marker.CommandText = "INSERT OR IGNORE INTO batches VALUES($id,$now)";
            marker.Parameters.AddWithValue("$id", batch.Id); marker.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (marker.ExecuteNonQuery() == 0) { tx.Commit(); return; }
            foreach (var row in batch.Rows)
            {
                Upsert(row, 0, tx); Upsert(row, 1, tx); Upsert(row, 2, tx);
            }
            foreach (var s in batch.Sessions)
            {
                using var c = db.CreateCommand(); c.Transaction = tx;
                c.CommandText = "INSERT OR IGNORE INTO sessions VALUES($id,$start,$end,$gross,$active,$reason)";
                c.Parameters.AddWithValue("$id", s.Id); c.Parameters.AddWithValue("$start", s.Start.ToString("O"));
                c.Parameters.AddWithValue("$end", s.End.ToString("O")); c.Parameters.AddWithValue("$gross", s.Gross);
                c.Parameters.AddWithValue("$active", s.ActiveMs); c.Parameters.AddWithValue("$reason", s.Reason); c.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }
    private void Upsert(MetricRow row, int resolution, SqliteTransaction tx)
    {
        using var c = db.CreateCommand(); c.Transaction = tx;
        var names = string.Join(",", Fields.Select(p => p.Name));
        var args = string.Join(",", Fields.Select(p => "$" + p.Name));
        var updates = string.Join(",", Fields.Select(p => $"{p.Name}={p.Name}+excluded.{p.Name}"));
        c.CommandText = $"INSERT INTO metrics(bucket,resolution,local_date,hour,offset,zone,app,profile,{names}) VALUES($bucket,$res,$date,$hour,$offset,$zone,$app,$profile,{args}) ON CONFLICT DO UPDATE SET {updates}";
        var k = row.Key;
        var bucket = resolution switch { 0 => k.UtcMinute, 1 => k.UtcMinute / 3600 * 3600, _ => 0 };
        c.Parameters.AddWithValue("$bucket", bucket); c.Parameters.AddWithValue("$res", resolution);
        c.Parameters.AddWithValue("$date", k.LocalDate); c.Parameters.AddWithValue("$hour", resolution == 2 ? -1 : k.Hour);
        c.Parameters.AddWithValue("$offset", k.Offset); c.Parameters.AddWithValue("$zone", k.Zone);
        c.Parameters.AddWithValue("$app", k.App); c.Parameters.AddWithValue("$profile", k.Profile);
        foreach (var p in Fields) c.Parameters.AddWithValue("$" + p.Name, p.GetValue(row.Counts)!);
        c.ExecuteNonQuery();
    }
    public IReadOnlyList<MetricRow> Read(string startDate, string endDate, int resolution = 1)
    {
        lock (gate)
        {
            using var c = db.CreateCommand();
            c.CommandText = "SELECT * FROM metrics WHERE resolution=$res AND local_date >= $start AND local_date <= $end ORDER BY local_date,hour";
            c.Parameters.AddWithValue("$res", resolution); c.Parameters.AddWithValue("$start", startDate); c.Parameters.AddWithValue("$end", endDate);
            using var r = c.ExecuteReader(); var list = new List<MetricRow>();
            while (r.Read())
            {
                var key = new BucketKey(r.GetInt64(0), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetString(5), r.GetString(6), r.GetString(7));
                var counts = new Counters(); foreach (var p in Fields) p.SetValue(counts, r.GetInt64(r.GetOrdinal(p.Name)));
                list.Add(new MetricRow(key, counts));
            }
            return list;
        }
    }
    public IReadOnlyList<TypingSession> Sessions(int limit = 100)
    {
        lock (gate)
        {
            using var c = db.CreateCommand(); c.CommandText = "SELECT * FROM sessions ORDER BY started DESC LIMIT $limit";
            c.Parameters.AddWithValue("$limit", limit); using var r = c.ExecuteReader(); var list = new List<TypingSession>();
            while (r.Read()) list.Add(new TypingSession(r.GetString(0), DateTimeOffset.Parse(r.GetString(1)), DateTimeOffset.Parse(r.GetString(2)), r.GetInt64(3), r.GetInt64(4), r.GetString(5)));
            return list;
        }
    }
    public Dictionary<string, string> Categories()
    {
        lock (gate)
        {
            using var c = db.CreateCommand(); c.CommandText = "SELECT app,category FROM applications";
            using var r = c.ExecuteReader(); var d = new Dictionary<string, string>(); while (r.Read()) d[r.GetString(0)] = r.GetString(1); return d;
        }
    }
    public void SetCategory(string app, string category)
    {
        lock (gate)
        {
            using var c = db.CreateCommand(); c.CommandText = "INSERT INTO applications VALUES($app,$category) ON CONFLICT(app) DO UPDATE SET category=excluded.category";
            c.Parameters.AddWithValue("$app", app); c.Parameters.AddWithValue("$category", category); c.ExecuteNonQuery();
        }
    }
    public void Maintain(int minuteDays = 90)
    {
        lock (gate)
        {
            using var c = db.CreateCommand();
            c.CommandText = "DELETE FROM metrics WHERE (resolution=0 AND local_date<$minute) OR (resolution=1 AND local_date<$hour); DELETE FROM sessions WHERE ended<$sessions;";
            c.Parameters.AddWithValue("$minute", DateTime.Today.AddDays(-minuteDays).ToString("yyyy-MM-dd"));
            c.Parameters.AddWithValue("$hour", DateTime.Today.AddYears(-2).ToString("yyyy-MM-dd"));
            c.Parameters.AddWithValue("$sessions", DateTimeOffset.UtcNow.AddYears(-2).ToString("O")); c.ExecuteNonQuery();
            // Retain batch markers: replay must remain idempotent even after a delayed retry.
        }
    }
    public void Backup(string destination)
    {
        if (System.IO.Path.GetFullPath(destination).Equals(Path, StringComparison.OrdinalIgnoreCase)) throw new IOException("Выберите другой файл для копии.");
        lock (gate)
        {
            using var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
            copy.Open(); db.BackupDatabase(copy);
        }
    }
    public void Restore(string source)
    {
        if (System.IO.Path.GetFullPath(source).Equals(Path, StringComparison.OrdinalIgnoreCase)) throw new IOException("Выберите резервную копию.");
        lock (gate)
        {
            using var from = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            from.Open(); using var c = from.CreateCommand(); c.CommandText = "PRAGMA quick_check";
            if ((string?)c.ExecuteScalar() != "ok") throw new InvalidDataException("Копия повреждена.");
            c.CommandText = "PRAGMA user_version";
            if (Convert.ToInt32(c.ExecuteScalar()) != 1) throw new InvalidDataException("Версия копии не поддерживается.");
            c.CommandText = "SELECT COUNT(*) FROM metrics"; c.ExecuteScalar();
            from.BackupDatabase(db);
        }
    }
    public void Clear()
    {
        lock (gate) Exec("BEGIN; DELETE FROM metrics; DELETE FROM sessions; DELETE FROM batches; COMMIT;");
    }
    public static void Export(IEnumerable<MetricRow> rows, string path)
    {
        static string Cell(string s)
        {
            if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        using var w = new StreamWriter(path, false, new UTF8Encoding(true));
        w.WriteLine("local_date,hour,offset_minutes,app,profile,gross_observed,gross_estimated,net_delta_estimate,backspaces,deletes,word_deletes,pastes,active_ms,unresolved,lost,metric_version");
        foreach (var row in rows)
        {
            var k = row.Key; var c = row.Counts;
            w.WriteLine(string.Join(",", k.LocalDate, k.Hour, k.Offset, Cell(k.App), Cell(k.Profile), c.Observed, c.Estimated, c.Net, c.Backspaces, c.Deletes, c.WordDeletes, c.Pastes, c.ActiveMs, c.Unresolved, c.Lost, "1"));
        }
    }
    public void Dispose() { lock (gate) db.Dispose(); }
}
