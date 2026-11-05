using Microsoft.Data.Sqlite;
using System.Text;
using System.Text.Json;

namespace BlueforsMonitor;

public static class BackendVerification
{
    public static void Run(string scratch)
    {
        Directory.CreateDirectory(scratch);
        string root = Path.Combine(scratch, "logs");
        Directory.CreateDirectory(root);
        string cache = Path.Combine(scratch, "cache");
        var store = LogImporter.OpenDirectory(root, cache);
        var importer = new LogImporter(store);
        var lines = new List<string>();
        double start = Clock.Unix(new DateTime(2026, 11, 5, 10, 0, 0));
        for (int i = 0; i < 50; i++)
        {
            double v = i >= 20 && i <= 22 ? .028 : .012;
            lines.Add($"05-11-26,10:{i:00}:00,{v.ToString(System.Globalization.CultureInfo.InvariantCulture)}\r\n");
        }
        string chamber = Path.Combine(root, "CH6 T 26-11-05.log");
        File.WriteAllText(chamber, string.Concat(lines), new UTF8Encoding(true));
        string flow = Path.Combine(root, "Flowmeter 26-11-05.log");
        File.WriteAllText(flow, "05-11-26,10:00:00,0.18");
        string pressure = Path.Combine(root, "maxigauge 26-11-05.log");
        File.WriteAllText(pressure, "05-11-26,10:00:00,CH1,P1,1,0.1,0,1,CH2,P2,0,2,0,1,CH3,P3,1,420,0,1,\r\n");
        var first = importer.Sync(true);
        Verification.Check(first.Added == 52, "Initial parse, BOM/CRLF, pressure status, or partial-line handling failed");
        Verification.Check(store.Snapshot.Events.Length == 1 && store.ReadChannel("Flow").Length == 0, "Initial event or partial line failed");
        long version = store.Revision;
        var idle = importer.Sync(true);
        Verification.Check(idle.Added == 0 && idle.Changed == 0 && store.Revision == version, "Unchanged scan should not redraw/recompute");
        File.AppendAllText(flow, "\r\n");
        File.AppendAllText(chamber, "05-11-26,10:50:00,0.013\r\n");
        var next = importer.Sync(true);
        Verification.Check(next.Added == 2 && store.ReadChannel("CH6").Length == 51 && store.ReadChannel("Flow").Length == 1, "Incremental append lost or duplicated samples");
        File.AppendAllText(chamber, "broken,row\r\n05-11-26,10:51:00,NaN\r\n");
        Verification.Check(importer.Sync(true).Skipped == 2, "Invalid rows and NaN must be skipped");

        Directory.CreateDirectory(Path.Combine(root, "a"));
        Directory.CreateDirectory(Path.Combine(root, "b"));
        string a = Path.Combine(root, "a", "CH5 T 26-11-05.log"), b = Path.Combine(root, "b", "CH5 T 26-11-05.log");
        File.WriteAllText(a, "05-11-26,10:00:00,0.69\n");
        File.WriteAllText(b, "05-11-26,10:00:00,0.70\n");
        importer.Sync(true);
        Verification.Check(store.ReadChannel("CH5").Single().V == .70, "Duplicate timestamp must have deterministic last-indexed value");
        File.WriteAllText(b, "");
        importer.Sync(true);
        Verification.Check(store.ReadChannel("CH5").Single().V == .69, "Truncation must preserve another source's sample");
        File.WriteAllText(chamber, "05-11-26,10:00:00,0.02\n05-11-26,10:01:00,0.02\n");
        importer.Sync(true);
        Verification.Check(store.ReadChannel("CH6").Length == 2 && store.Snapshot.Events.Length == 0, "File replacement must remove stale samples and events");
        using (var db = store.Open())
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT k,count(*) FROM samples GROUP BY k";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                Verification.Check(store.Snapshot.Counts[r.GetString(0)] == r.GetInt32(1), "Snapshot counts must match committed DB");
        }
        File.WriteAllText(Path.Combine(store.Folder, "pending-refresh.json"), JsonSerializer.Serialize(new[] { (int)(start / 86400) }, MonitorJsonContext.Default.Int32Array));
        version = store.Revision;
        importer.Sync(true);
        Verification.Check(store.Revision > version && !File.Exists(Path.Combine(store.Folder, "pending-refresh.json")), "Interrupted metadata refresh must be recoverable");
        var restart = LogImporter.OpenDirectory(root, cache);
        Verification.Check(restart.Snapshot.Counts["CH6"] == 2, "Cache restart failed");
        Console.WriteLine("PASS append / partial UTF-8 BOM + CRLF / invalid rows / unchanged scan / duplicate source / truncation / event invalidation / interrupted refresh / restart");
    }
}
