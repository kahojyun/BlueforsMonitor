using BlueforsAvalonia;
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
            // Simulate a released legacy cache, preserving provenance and file positions.
            command.CommandText = """
                BEGIN;
                ALTER TABLE samples RENAME TO old_samples;
                CREATE TABLE samples(t REAL,k TEXT,v REAL,PRIMARY KEY(t,k));
                INSERT INTO samples SELECT * FROM old_samples; DROP TABLE old_samples;
                ALTER TABLE source_samples RENAME TO old_source_samples;
                CREATE TABLE source_samples(file_id INTEGER,t REAL,k TEXT,v REAL,PRIMARY KEY(file_id,t,k));
                INSERT INTO source_samples SELECT * FROM old_source_samples; DROP TABLE old_source_samples;
                CREATE INDEX channel_times ON samples(k,t); CREATE INDEX source_times ON source_samples(t,k);
                COMMIT;
                """;
            command.ExecuteNonQuery();
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
        Verification.Check(store.ReadChannel("CH6").Single().V == .02, "File replacement remains correct after migration");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            importer.Compact(cancelled.Token);
            throw new Exception("Expected cancellation");
        }
        catch (OperationCanceledException) { }
        Verification.Check(store.ReadChannel("CH6").Single().V == .02, "Cancelled maintenance preserves data");
        Console.WriteLine("PASS storage: compact schema, legacy migration, provenance, offsets, integrity, replacement, cancellation, WAL truncation");
    }
}
