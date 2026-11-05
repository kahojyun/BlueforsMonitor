using BlueforsMonitor;

static class MonitoringSessionChecks
{
    public static async Task Run(string scratch)
    {
        var store = new DataStore(Path.Combine(scratch, "session-initial"));
        store.Snapshot = store.Snapshot with
        {
            Counts = DataStore.Keys.ToDictionary(k => k, _ => 1),
            Bounds = [0, 10000]
        };
        var reads = new List<(TaskCompletionSource<WindowData> Completion, CancellationToken Token)>();
        var cursors = new List<TaskCompletionSource<Dictionary<string, Sample[]>>>();
        using var session = new MonitoringSession(store, Path.Combine(scratch, "session-cache"),
            (_, _, _, _, token) =>
            {
                var completion = new TaskCompletionSource<WindowData>();
                reads.Add((completion, token));
                return completion.Task;
            }, (_, _) =>
            {
                var completion = new TaskCompletionSource<Dictionary<string, Sample[]>>();
                cursors.Add(completion);
                return completion.Task;
            });
        WindowData Data(double left, double right) => new(DataStore.Keys.ToDictionary(k => k, _ => new SeriesData([new(100, 1)], [])), left, right, 0, 1);
        Dictionary<string, Sample[]> Samples(double value) => DataStore.Keys.ToDictionary(k => k, _ => new[] { new Sample(100, value) });
        var old = session.LoadAsync(0, 1000);
        var latest = session.LoadAsync(2000, 3000);
        var current = Data(1800, 3200);
        reads[1].Completion.SetResult(current);
        Verification.Check((await latest)?.Changed == true, "Latest query accepted");
        reads[0].Completion.SetException(new IOException("obsolete failure"));
        Verification.Check(await old == null && ReferenceEquals(session.Data, current) && reads[0].Token.IsCancellationRequested, "Old failure cannot replace current data or escape cancellation");
        var pending = session.LoadAsync(5000, 6000);
        var cached = await session.LoadAsync(2100, 2900);
        Verification.Check(cached?.Changed == false && reads.Count == 3, "Covered view reuses cache");
        reads[2].Completion.SetResult(Data(4800, 6200));
        Verification.Check(await pending == null && ReferenceEquals(session.Data, current), "Cache hit supersedes pending pan");
        var outdated = session.LoadAsync(2000, 3000, true);
        store.Revision++;
        reads[3].Completion.SetResult(current);
        Verification.Check(await outdated == null, "Revision change rejects display query");
        var cursor = session.ReadCursorAsync(100);
        Verification.Check(await session.ReadCursorAsync(101) == null, "Only one cursor query at a time");
        store.Revision++;
        cursors[0].SetResult(Samples(1));
        Verification.Check(await cursor == null, "Revision change rejects cursor query before caching");
        var freshCursor = session.ReadCursorAsync(100);
        cursors[1].SetResult(Samples(2));
        Verification.Check((await freshCursor)?["CH6"][0].V == 2, "Cursor retries after stale result");
        Verification.Check((await session.ReadCursorAsync(101))?["CH6"][0].V == 2 && cursors.Count == 2, "Cursor bucket reused");
        var oldSourceRead = session.LoadAsync(0, 1000, true);
        var oldSourceCursor = session.ReadCursorAsync(1000);
        string logs = Path.Combine(scratch, "session-logs");
        Directory.CreateDirectory(logs);
        Verification.Check(await session.OpenAsync(logs) != null && session.IsMonitoring && session.Data == null, "Empty live directory opens and clears display cache");
        reads[4].Completion.SetResult(Data(0, 1000));
        cursors[2].SetResult(Samples(3));
        Verification.Check(await oldSourceRead == null && await oldSourceCursor == null, "Source switch rejects display and cursor completions");
        var active = session.Store;
        bool failed = false;
        try
        {
            await session.OpenAsync(Path.Combine(scratch, "missing-session-logs"));
        }
        catch (DirectoryNotFoundException) { failed = true; }
        Verification.Check(failed && ReferenceEquals(active, session.Store), "Failed directory open retains current source");
        File.WriteAllText(Path.Combine(logs, "CH6 T 26-11-05.log"), "05-11-26,10:00:00,0.012\n");
        var refresh = await session.RefreshAsync(true);
        Verification.Check(refresh?.Changed == true && session.Store.HasData, "Empty live directory picks up new logs");
        Verification.Check((await session.RefreshAsync(true))?.Changed == false, "Unchanged refresh preserves revision");
        var closingRead = session.LoadAsync(0, 1000, true);
        var closingCursor = session.ReadCursorAsync(100);
        session.Dispose();
        reads[5].Completion.SetException(new IOException("closed display"));
        cursors[3].SetException(new IOException("closed cursor"));
        Verification.Check(await closingRead == null && await closingCursor == null, "Close suppresses late data and errors");
        Verification.Check(await session.LoadAsync(0, 1000) == null && await session.OpenAsync(logs) == null && await session.RefreshAsync(true) == null, "Disposed session does not restart work");
        File.AppendAllText(Path.Combine(logs, "CH6 T 26-11-05.log"), "05-11-26,10:01:00,0.013\n");
        using var reopened = new MonitoringSession(new DataStore(Path.Combine(scratch, "preview-empty")), Path.Combine(scratch, "session-cache"));
        bool previewed = false;
        await reopened.OpenAsync(logs, null, async () =>
        {
            previewed = true;
            Verification.Check(reopened.Store.Snapshot.Counts["CH6"] == 1, "Preview uses existing snapshot before new logs are scanned");
            var bounds = reopened.Store.Snapshot.Bounds;
            var preview = await reopened.LoadAsync(bounds[0] - 60, bounds[1] + 60);
            Verification.Check(preview?.Data.Count == 1, "Cached data is queryable before scan");
            Verification.Check(await reopened.RefreshAsync(true) == null, "Timer cannot start another scan during cached preview");
        });
        Verification.Check(previewed && reopened.Store.Snapshot.Counts["CH6"] == 2, "Background open scan picks up newer records after preview");
        Console.WriteLine("PASS cached preview before scan and subsequent update");
        Console.WriteLine("PASS monitoring session: query ordering, cache-hit cancellation, revision/source isolation, cursor cache, empty live source, failed open, disposal");
    }
}
