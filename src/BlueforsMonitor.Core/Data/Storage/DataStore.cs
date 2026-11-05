using Microsoft.Data.Sqlite;
using System.IO.Compression;
using System.Text.Json;

namespace BlueforsMonitor;

public sealed class DataStore
{
    public static readonly string[] Keys = ["CH6", "CH5", "Flow", "P2", "P3", "P4", "CH1", "CH2", "P1", "P5", "P6"];
    public string Folder
    {
        get;
    }
    public string DatabasePath => Path.Combine(Folder, "history.sqlite");
    public string? LogRoot
    {
        get; internal set;
    }
    public bool IsLive => LogRoot != null;
    public long Revision
    {
        get; internal set;
    }
    public bool HasData => Snapshot.Counts.Values.Sum() > 0;
    public Snapshot Snapshot
    {
        get; internal set;
    }
    public DataStore(string folder)
    {
        Folder = folder;
        Snapshot = File.Exists(Path.Combine(folder, "events.json"))
            ? JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(folder, "events.json")), MonitorJsonContext.Default.Snapshot)!
            : new(StorageFormat.Version, "", "Asia/Shanghai", [0, 0], Keys.ToDictionary(k => k, _ => 0), []);
        if (Snapshot is null || Snapshot.FormatVersion != StorageFormat.Version)
            throw new InvalidDataException("索引事件格式不受支持，请重新建立本机索引。");
    }
    internal SqliteConnection Open(bool write = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Mode = write ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadOnly, Pooling = true, DefaultTimeout = 5 }.ToString());
        connection.Open();
        return connection;
    }
    // Never hold up live monitoring for a reader in another process.
    internal void Checkpoint()
    {
        try
        {
            using var db = Open(true);
            using var command = db.CreateCommand();
            command.CommandTimeout = 1;
            command.CommandText = "PRAGMA busy_timeout=0; PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6) { }
    }
    internal void Compact(CancellationToken token)
    {
        using var db = Open(true);
        token.ThrowIfCancellationRequested();
        using var vacuum = db.CreateCommand();
        vacuum.CommandTimeout = 0;
        vacuum.CommandText = "VACUUM; PRAGMA optimize;";
        vacuum.ExecuteNonQuery();
        Checkpoint();
    }
    public static int ResolutionFor(double span) => span <= 3 * 86400 ? 0 : span <= 35 * 86400 ? 1800 : 21600;
    public WindowData Read(double left, double right, double span, CancellationToken token)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var resolution = ResolutionFor(span);
        var points = Keys.ToDictionary(k => k, _ => new List<Sample>());
        var gaps = Keys.ToDictionary(k => k, _ => new HashSet<double>());
        using var connection = Open();
        if (resolution == 0)
        {
            ReadRaw(connection, left, right, points, token);
            foreach (var key in Keys)
                for (int i = 1; i < points[key].Count; i++)
                    if (points[key][i].T - points[key][i - 1].T > 180)
                        gaps[key].Add(points[key][i].T);
        }
        else
        {
            int first = (int)Math.Floor(left / 86400), last = (int)Math.Floor(right / 86400);
            var covered = new HashSet<int>();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT day,payload FROM overview WHERE day BETWEEN $a AND $b ORDER BY day";
            command.Parameters.AddWithValue("$a", first);
            command.Parameters.AddWithValue("$b", last);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    int day = reader.GetInt32(0);
                    using var input = new MemoryStream((byte[])reader[1]);
                    using var zip = new ZLibStream(input, CompressionMode.Decompress);
                    using var json = JsonDocument.Parse(zip);
                    if (!json.RootElement.TryGetProperty(resolution.ToString(), out var level))
                        continue;
                    covered.Add(day);
                    foreach (var key in Keys)
                    {
                        if (!level.TryGetProperty(key, out var data))
                            continue;
                        foreach (var p in data[0].EnumerateArray())
                        {
                            var t = p[0].GetDouble();
                            if (t >= left && t <= right)
                                points[key].Add(new(t, p[1].GetDouble()));
                        }
                        foreach (var gap in data[1].EnumerateArray())
                            gaps[key].Add(gap.GetDouble());
                    }
                }
            }
            // Recover missing overview days from raw samples without changing the DB.
            for (int day = first; day <= last; day++)
            {
                token.ThrowIfCancellationRequested();
                if (covered.Contains(day))
                    continue;
                var raw = Keys.ToDictionary(k => k, _ => new List<Sample>());
                ReadRaw(connection, Math.Max(left, day * 86400d), Math.Min(right, (day + 1) * 86400d - .001), raw, token);
                foreach (var key in Keys)
                {
                    var reduced = Reduce(raw[key], resolution);
                    points[key].AddRange(reduced.Points);
                    gaps[key].UnionWith(reduced.Gaps);
                }
            }
        }
        var series = Keys.ToDictionary(k => k, k =>
        {
            var sorted = points[k].OrderBy(p => p.T).ToArray();
            for (int i = 1; i < sorted.Length; i++)
                if (Math.Floor(sorted[i].T / 86400) != Math.Floor(sorted[i - 1].T / 86400) && sorted[i].T - sorted[i - 1].T > 180)
                    gaps[k].Add(sorted[i].T);
            return new SeriesData(sorted, gaps[k]);
        });
        return new(series, left, right, resolution, timer.Elapsed.TotalMilliseconds);
    }
    static void ReadRaw(SqliteConnection connection, double left, double right, Dictionary<string, List<Sample>> points, CancellationToken token)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT t,k,v FROM samples WHERE t BETWEEN $a AND $b ORDER BY t";
        command.Parameters.AddWithValue("$a", left);
        command.Parameters.AddWithValue("$b", right);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (points.TryGetValue(reader.GetString(1), out var series))
                series.Add(new(reader.GetDouble(0), reader.GetDouble(2)));
        }
    }
    public static SeriesData Reduce(IReadOnlyList<Sample> input, int width)
    {
        var result = new List<Sample>();
        var gaps = new HashSet<double>();
        for (int start = 0; start < input.Count;)
        {
            int end = start + 1;
            while (end < input.Count && Math.Floor(input[end].T / width) == Math.Floor(input[start].T / width) && input[end].T - input[end - 1].T <= 180)
                end++;
            int min = start, max = start;
            for (int i = start + 1; i < end; i++)
            {
                if (input[i].V < input[min].V)
                    min = i;
                if (input[i].V > input[max].V)
                    max = i;
            }
            foreach (int i in new[] { start, min, max, end - 1 }.Distinct().Order())
                result.Add(input[i]);
            if (end < input.Count && input[end].T - input[end - 1].T > 180)
                gaps.Add(input[end].T);
            start = end;
        }
        return new(result.ToArray(), gaps);
    }
    public Dictionary<string, Sample[]> ReadCursorBucket(long bucket)
    {
        using var connection = Open();
        var values = Keys.ToDictionary(k => k, _ => new List<Sample>());
        ReadRaw(connection, bucket * 300 - 90, (bucket + 1) * 300 + 90, values, CancellationToken.None);
        return values.ToDictionary(p => p.Key, p => p.Value.ToArray());
    }
    public static Sample? Nearest(Sample[] points, double time)
    {
        int lo = 0, hi = points.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (points[mid].T < time)
                lo = mid + 1;
            else
                hi = mid;
        }
        Sample? best = lo < points.Length ? points[lo] : null;
        if (lo > 0 && (best is null || time - points[lo - 1].T < Math.Abs(best.T - time)))
            best = points[lo - 1];
        return best is not null && Math.Abs(best.T - time) <= 90 ? best : null;
    }
    public Sample[] ReadChannel(string key, double from = double.MinValue)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT t,v FROM samples WHERE k=$k AND t>=$a ORDER BY t";
        command.Parameters.AddWithValue("$k", key);
        command.Parameters.AddWithValue("$a", from);
        using var reader = command.ExecuteReader();
        var points = new List<Sample>();
        while (reader.Read())
            points.Add(new(reader.GetDouble(0), reader.GetDouble(1)));
        return points.ToArray();
    }
    internal void SaveSnapshot()
    {
        var file = Path.Combine(Folder, "events.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(Snapshot, MonitorJsonContext.Default.Snapshot));
        File.Move(file + ".tmp", file, true);
    }
    internal Dictionary<string, Sample[]> ReadRange(double left, double right)
    {
        using var connection = Open();
        var values = Keys.ToDictionary(k => k, _ => new List<Sample>());
        ReadRaw(connection, left, right, values, CancellationToken.None);
        return values.ToDictionary(p => p.Key, p => p.Value.ToArray());
    }
}
