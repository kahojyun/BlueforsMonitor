using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BlueforsAvalonia;

public record LogSample(double T, string K, double V);
public record SyncResult(int Added, int Changed, int Skipped, double DirtyFrom, double DirtyTo, int Files);

public sealed class LogImporter
{
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    readonly DataStore _store;
    readonly LogParser _parser = new();
    DateTime _lastFullScan = DateTime.MinValue;
    public LogImporter(DataStore store) => _store = store;
    public static DataStore OpenDirectory(string root, string cacheRoot)
    {
        string normalized = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(normalized))
            throw new DirectoryNotFoundException(normalized);
        if (OperatingSystem.IsWindows())
            normalized = normalized.ToUpperInvariant();
        string folder = Path.Combine(cacheRoot, "datasets", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..20]);
        Directory.CreateDirectory(folder);
        var store = new DataStore(folder) { LogRoot = Path.GetFullPath(root) };
        using var db = store.Open(true);
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS samples(t REAL,k TEXT,v REAL,PRIMARY KEY(t,k)) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS files(path TEXT PRIMARY KEY,pos INTEGER,mtime REAL);
            CREATE TABLE IF NOT EXISTS overview(day INTEGER PRIMARY KEY,payload BLOB);
            CREATE TABLE IF NOT EXISTS source_files(id INTEGER PRIMARY KEY,path TEXT UNIQUE,position INTEGER NOT NULL DEFAULT 0,size INTEGER NOT NULL DEFAULT 0,mtime INTEGER NOT NULL DEFAULT 0,head TEXT NOT NULL DEFAULT '');
            CREATE TABLE IF NOT EXISTS source_samples(file_id INTEGER,t REAL,k TEXT,v REAL,PRIMARY KEY(file_id,t,k)) WITHOUT ROWID;
            CREATE INDEX IF NOT EXISTS source_times ON source_samples(t,k);
            CREATE INDEX IF NOT EXISTS channel_times ON samples(k,t);
            """;
        cmd.ExecuteNonQuery();
        return store;
    }
    public List<LogSample> Parse(string name, string line) => _parser.Parse(name, line);
    static IEnumerable<(long End, string Text)> CompleteLines(string file, long offset, CancellationToken token)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan);
        stream.Seek(offset, SeekOrigin.Begin);
        byte[] buffer = new byte[65536];
        using var line = new MemoryStream();
        long end = offset;
        int length;
        while ((length = stream.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            int start = 0;
            for (int i = 0; i < length; i++)
                if (buffer[i] == 10)
                {
                    line.Write(buffer, start, i - start);
                    end += i - start + 1;
                    yield return (end, Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r'));
                    line.SetLength(0);
                    start = i + 1;
                }
            if (start < length)
            {
                line.Write(buffer, start, length - start);
                end += length - start;
            }
            if (line.Length > 8 * 1024 * 1024)
                throw new InvalidDataException("日志行超过 8 MB：" + file);
        }
        // Deliberately do not commit a trailing partial line. It is reread on the next poll.
    }
    static bool Recognized(string path)
    {
        var name = Path.GetFileName(path);
        return LogParser.ChannelPattern.IsMatch(name) || name.StartsWith("Flowmeter ") || name.StartsWith("maxigauge ");
    }
    IEnumerable<string> Files(bool full)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint };
        if (full)
            return Directory.EnumerateFiles(_store.LogRoot!, "*", options).Where(Recognized).Order(StringComparer.Ordinal);
        var days = Directory.EnumerateDirectories(_store.LogRoot!).Where(p => Regex.IsMatch(Path.GetFileName(p), @"^\d\d-\d\d-\d\d$")).Order(StringComparer.Ordinal).TakeLast(2);
        return Directory.EnumerateFiles(_store.LogRoot!).Concat(days.SelectMany(p => Directory.EnumerateFiles(p, "*", options))).Where(Recognized).Order(StringComparer.Ordinal);
    }
    static string Head(string file, int size)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] bytes = new byte[size];
        int got = stream.Read(bytes);
        return Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, got)));
    }
    public SyncResult Sync(bool forceFull = false, IProgress<string>? progress = null, CancellationToken token = default)
    {
        var gate = Gates.GetOrAdd(_store.Folder, _ => new SemaphoreSlim(1));
        gate.Wait(token);
        try
        {
            var result = SyncCore(forceFull, progress, token);
            _store.Checkpoint();
            return result;
        }
        finally { gate.Release(); }
    }
    public void Compact(CancellationToken token = default)
    {
        var gate = Gates.GetOrAdd(_store.Folder, _ => new SemaphoreSlim(1));
        gate.Wait(token);
        try
        {
            _store.Compact(token);
        }
        finally { gate.Release(); }
    }
    SyncResult SyncCore(bool forceFull, IProgress<string>? progress, CancellationToken token)
    {
        bool full = forceFull || DateTime.UtcNow - _lastFullScan > TimeSpan.FromMinutes(5);
        var files = Files(full).ToArray();
        int added = 0, changed = 0, skipped = 0, processed = 0;
        double dirtyFrom = double.PositiveInfinity, dirtyTo = double.NegativeInfinity;
        var dirtyDays = new HashSet<int>();
        var counts = new Dictionary<string, int>(_store.Snapshot.Counts);
        string pending = Path.Combine(_store.Folder, "pending-refresh.json");
        // If an earlier scan committed but overview/event generation was interrupted, recover
        // from the durable database rather than silently trusting stale JSON metadata.
        if (File.Exists(pending))
        {
            var previous = JsonSerializer.Deserialize(File.ReadAllText(pending), MonitorJsonContext.Default.Int32Array) ?? [];
            dirtyDays.UnionWith(previous);
            if (previous.Length > 0)
            {
                dirtyFrom = previous.Min() * 86400d;
                dirtyTo = (previous.Max() + 1) * 86400d;
            }
        }
        foreach (var key in DataStore.Keys)
            counts.TryAdd(key, 0);
        using var db = _store.Open(true);
        if (File.Exists(pending))
        {
            counts = DataStore.Keys.ToDictionary(k => k, _ => 0);
            using var counter = db.CreateCommand();
            counter.CommandText = "SELECT k,count(*) FROM samples GROUP BY k";
            using var r = counter.ExecuteReader();
            while (r.Read())
                counts[r.GetString(0)] = r.GetInt32(1);
        }
        using var tx = db.BeginTransaction();
        using (var temporary = db.CreateCommand())
        {
            temporary.Transaction = tx;
            temporary.CommandText = "CREATE TEMP TABLE IF NOT EXISTS affected(t REAL,k TEXT,PRIMARY KEY(t,k))";
            temporary.ExecuteNonQuery();
        }
        SqliteCommand Command(string sql, params string[] names)
        {
            var c = db.CreateCommand();
            c.Transaction = tx;
            c.CommandText = sql;
            foreach (var n in names)
                c.Parameters.Add(new SqliteParameter(n, DBNull.Value));
            c.Prepare();
            return c;
        }
        using var meta = Command("SELECT id,position,size,mtime,head FROM source_files WHERE path=$p", "$p");
        using var ensure = Command("INSERT OR IGNORE INTO source_files(path) VALUES($p)", "$p");
        using var provenance = Command("INSERT INTO source_samples VALUES($id,$t,$k,$v) ON CONFLICT(file_id,t,k) DO UPDATE SET v=excluded.v", "$id", "$t", "$k", "$v");
        using var insert = Command("INSERT OR IGNORE INTO samples VALUES($t,$k,$v)", "$t", "$k", "$v");
        using var update = Command("UPDATE samples SET v=$v WHERE t=$t AND k=$k AND v<>$v", "$t", "$k", "$v");
        using var save = Command("UPDATE source_files SET position=$pos,size=$size,mtime=$mtime,head=$head WHERE id=$id", "$pos", "$size", "$mtime", "$head", "$id");
        using var compatibility = Command("INSERT INTO files VALUES($path,$pos,$mtime) ON CONFLICT(path) DO UPDATE SET pos=excluded.pos,mtime=excluded.mtime", "$path", "$pos", "$mtime");
        void Dirty(double t)
        {
            dirtyFrom = Math.Min(dirtyFrom, t);
            dirtyTo = Math.Max(dirtyTo, t);
            dirtyDays.Add((int)Math.Floor(t / 86400));
        }
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(file);
                long length = info.Length, mtime = info.LastWriteTimeUtc.Ticks;
                ensure.Parameters[0].Value = file;
                ensure.ExecuteNonQuery();
                meta.Parameters[0].Value = file;
                long id, pos, oldSize, oldMtime;
                string head;
                using (var r = meta.ExecuteReader())
                {
                    r.Read();
                    id = r.GetInt64(0);
                    pos = r.GetInt64(1);
                    oldSize = r.GetInt64(2);
                    oldMtime = r.GetInt64(3);
                    head = r.GetString(4);
                }
                if (length == oldSize && mtime == oldMtime)
                    continue;
                bool reset = length < pos || (length == oldSize && oldMtime != 0 && mtime != oldMtime);
                if (!reset && pos > 0 && head != "")
                    reset = Head(file, (int)Math.Min(128, oldSize)) != head;
                if (reset)
                {
                    using (var affected = Command("SELECT s.t,s.k FROM samples s JOIN source_samples p ON s.t=p.t AND s.k=p.k WHERE p.file_id=$id", "$id"))
                    {
                        affected.Parameters[0].Value = id;
                        using var r = affected.ExecuteReader();
                        while (r.Read())
                        {
                            Dirty(r.GetDouble(0));
                            counts[r.GetString(1)]--;
                            changed++;
                        }
                    }
                    using (var clear = Command("""
                        CREATE TEMP TABLE IF NOT EXISTS affected(t REAL,k TEXT,PRIMARY KEY(t,k)); DELETE FROM affected;
                        INSERT INTO affected SELECT t,k FROM source_samples WHERE file_id=$id;
                        DELETE FROM samples WHERE (t,k) IN (SELECT t,k FROM affected);
                        DELETE FROM source_samples WHERE file_id=$id;
                        INSERT OR REPLACE INTO samples SELECT p.t,p.k,p.v FROM source_samples p JOIN affected a ON p.t=a.t AND p.k=a.k ORDER BY p.file_id;
                        """, "$id"))
                    {
                        clear.Parameters[0].Value = id;
                        clear.ExecuteNonQuery();
                    }
                    using (var restore = Command("SELECT s.k,count(*) FROM samples s JOIN affected a ON s.t=a.t AND s.k=a.k GROUP BY s.k"))
                    {
                        using var r = restore.ExecuteReader();
                        while (r.Read())
                            counts[r.GetString(0)] += r.GetInt32(1);
                    }
                    pos = 0;
                }
                long committed = pos;
                foreach (var line in CompleteLines(file, pos, token))
                {
                    var rows = Parse(Path.GetFileName(file), line.Text);
                    if (rows.Count == 0)
                        skipped++;
                    foreach (var row in rows)
                    {
                        provenance.Parameters[0].Value = id;
                        provenance.Parameters[1].Value = row.T;
                        provenance.Parameters[2].Value = row.K;
                        provenance.Parameters[3].Value = row.V;
                        provenance.ExecuteNonQuery();
                        insert.Parameters[0].Value = row.T;
                        insert.Parameters[1].Value = row.K;
                        insert.Parameters[2].Value = row.V;
                        if (insert.ExecuteNonQuery() > 0)
                        {
                            added++;
                            counts[row.K]++;
                            Dirty(row.T);
                        }
                        else
                        {
                            update.Parameters["$t"].Value = row.T;
                            update.Parameters["$k"].Value = row.K;
                            update.Parameters["$v"].Value = row.V;
                            if (update.ExecuteNonQuery() > 0)
                            {
                                changed++;
                                Dirty(row.T);
                            }
                        }
                    }
                    committed = line.End;
                }
                save.Parameters[0].Value = committed;
                save.Parameters[1].Value = length;
                save.Parameters[2].Value = mtime;
                save.Parameters[3].Value = Head(file, (int)Math.Min(128, length));
                save.Parameters[4].Value = id;
                save.ExecuteNonQuery();
                compatibility.Parameters[0].Value = file;
                compatibility.Parameters[1].Value = committed;
                compatibility.Parameters[2].Value = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds() / 1000d;
                compatibility.ExecuteNonQuery();
                processed++;
                if (processed % 30 == 0)
                    progress?.Report($"已索引 {processed:N0} 个日志文件 · 新增 {added:N0} 条");
            }
            catch (IOException) { skipped++; }
            catch (UnauthorizedAccessException) { skipped++; }
        }
        using (var invalid = Command("DELETE FROM overview WHERE day=$d", "$d"))
            foreach (int day in dirtyDays)
            {
                invalid.Parameters[0].Value = day;
                invalid.ExecuteNonQuery();
            }
        if (dirtyDays.Count > 0)
            File.WriteAllText(pending, JsonSerializer.Serialize(dirtyDays.Order().ToArray(), MonitorJsonContext.Default.Int32Array));
        tx.Commit();
        if (full)
            _lastFullScan = DateTime.UtcNow;
        if (dirtyDays.Count > 0)
        {
            // Rebuild only changed days; payload is compatible with the Python overview cache.
            using var build = db.CreateCommand();
            build.CommandText = "INSERT OR REPLACE INTO overview VALUES($d,$p)";
            build.Parameters.Add("$d", SqliteType.Integer);
            build.Parameters.Add("$p", SqliteType.Blob);
            build.Prepare();
            int built = 0;
            foreach (int day in dirtyDays.Order())
            {
                token.ThrowIfCancellationRequested();
                if (built++ % 10 == 0)
                    progress?.Report($"正在生成历史概览 {built} / {dirtyDays.Count} 天…");
                var raw = _store.ReadRange(day * 86400d, (day + 1) * 86400d - .001);
                using var bytes = new MemoryStream();
                using (var zip = new ZLibStream(bytes, CompressionLevel.Fastest, true))
                using (var json = new Utf8JsonWriter(zip))
                {
                    // Keep the existing [points, gaps] payload schema exactly,
                    // without reflection or object-typed polymorphic serialization.
                    json.WriteStartObject();
                    foreach (int width in new[] { 1800, 21600 })
                    {
                        json.WriteStartObject(width.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        foreach (var (key, values) in raw.Where(kv => kv.Value.Length > 0))
                        {
                            var reduced = DataStore.Reduce(values, width);
                            json.WriteStartArray(key);
                            json.WriteStartArray();
                            foreach (var point in reduced.Points)
                            {
                                json.WriteStartArray();
                                json.WriteNumberValue(point.T);
                                json.WriteNumberValue(point.V);
                                json.WriteEndArray();
                            }
                            json.WriteEndArray();
                            json.WriteStartArray();
                            foreach (var gap in reduced.Gaps.Order()) json.WriteNumberValue(gap);
                            json.WriteEndArray();
                            json.WriteEndArray();
                        }
                        json.WriteEndObject();
                    }
                    json.WriteEndObject();
                }
                build.Parameters[0].Value = day;
                build.Parameters[1].Value = bytes.ToArray();
                build.ExecuteNonQuery();
            }
            using var bounds = db.CreateCommand();
            bounds.CommandText = "SELECT min(t),max(t) FROM samples";
            double[] range;
            using (var r = bounds.ExecuteReader())
            {
                r.Read();
                range = r.IsDBNull(0) ? [0, 0] : [r.GetDouble(0), r.GetDouble(1)];
            }
            progress?.Report("正在更新升温事件…");
            var old = _store.Snapshot.Events;
            var anchor = old.FirstOrDefault(e => e.Recovered && e.End < dirtyFrom - 1800);
            double cutoff = anchor?.End ?? range[0] - 1;
            var events = EventDetector.Detect(_store.ReadChannel("CH6", cutoff - 1800), token).Where(e => e.Start > cutoff).ToList();
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                var nearby = _store.ReadRange(e.Start - 900, e.End);
                events[i] = e with
                {
                    Detail = "相对事前15分钟中位数的最大偏移 · " + string.Join(" / ", new[] { "CH5", "Flow", "P2", "P3", "P4" }.Select(k => k + " " + EventDetector.Change(nearby[k], e.Start, e.End)))
                };
            }
            if (anchor != null)
                events.AddRange(old.SkipWhile(e => e.Start != anchor.Start));
            _store.Snapshot = new(_store.LogRoot!, "Asia/Shanghai", "Bluefors Monitor 0.15 (C# port)", "", range, counts, events.ToArray());
            _store.SaveSnapshot();
            _store.Revision++;
            File.Delete(pending);
        }
        return new(added, changed, skipped, dirtyFrom, dirtyTo, processed);
    }
}
