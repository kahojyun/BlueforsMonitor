using BlueforsMonitor;
using Microsoft.Data.Sqlite;

static class StorageChecks
{
    public static void Run(string scratch)
    {
        string logs = Path.Combine(scratch, "storage-logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "CH6 T 26-11-05.log"), "05-11-26,10:00:00,0.012\n05-11-26,10:01:00,0.014\n");
        var store = LogImporter.OpenDirectory(logs, Path.Combine(scratch, "storage-cache"));
        var importer = new LogImporter(store);
        importer.Sync(true);
        var original = store.ReadChannel("CH6");
        Verification.Check(!File.Exists(store.DatabasePath + "-wal") || new FileInfo(store.DatabasePath + "-wal").Length == 0, "Idle WAL is truncated after scan");
        using (var db = store.Open(true))
        {
            using var command = db.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE name IN ('samples','source_samples') AND sql LIKE '%WITHOUT ROWID%'";
            Verification.Check(Convert.ToInt32(command.ExecuteScalar()) == 2, "New caches use compact primary-key storage");
            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='files'";
            Verification.Check(Convert.ToInt32(command.ExecuteScalar()) == 0, "No Python compatibility table");
            command.CommandText = "PRAGMA user_version";
            Verification.Check(Convert.ToInt32(command.ExecuteScalar()) == StorageFormat.Version, "Explicit database version");
        }
        importer.Compact();
        Verification.Check(original.SequenceEqual(store.ReadChannel("CH6")), "Compaction preserves original samples");
        using (var db = store.Open())
        {
            using var command = db.CreateCommand();
            command.CommandText = "PRAGMA integrity_check";
            Verification.Check((string?)command.ExecuteScalar() == "ok", "Compacted DB integrity");
            command.CommandText = "SELECT count(*) FROM source_samples";
            Verification.Check(Convert.ToInt32(command.ExecuteScalar()) == 2, "Compaction preserves provenance");
        }
        Verification.Check(importer.Sync(true).Added == 0, "Compaction preserves file offsets");
        File.WriteAllText(Path.Combine(logs, "CH6 T 26-11-05.log"), "05-11-26,10:00:00,0.02\n");
        importer.Sync(true);
        Verification.Check(store.ReadChannel("CH6").Single().V == .02, "File replacement remains correct after compaction");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            importer.Compact(cancelled.Token);
            throw new Exception("Expected cancellation");
        }
        catch (OperationCanceledException) { }
        Verification.Check(store.ReadChannel("CH6").Single().V == .02, "Cancelled maintenance preserves data");
        foreach (int unsupported in new[] { 0, 999 })
        {
            using (var db = store.Open(true))
            using (var command = db.CreateCommand())
            {
                command.CommandText = $"PRAGMA user_version={unsupported}";
                command.ExecuteNonQuery();
            }
            try
            {
                LogImporter.OpenDirectory(logs, Path.Combine(scratch, "storage-cache"));
                throw new Exception("Unsupported database format accepted");
            }
            catch (InvalidDataException) { }
            using var check = store.Open();
            using var version = check.CreateCommand();
            version.CommandText = "PRAGMA user_version";
            Verification.Check(Convert.ToInt32(version.ExecuteScalar()) == unsupported, "Rejected database is not upgraded");
            Verification.Check(store.ReadChannel("CH6").Single().V == .02, "Rejected database retains samples");
        }
        Console.WriteLine("PASS storage: compact schema, explicit version, provenance, offsets, integrity, replacement, cancellation, WAL truncation");
    }
}
