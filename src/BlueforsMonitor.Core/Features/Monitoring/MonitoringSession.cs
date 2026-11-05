namespace BlueforsMonitor;

public sealed record DisplayResult(WindowData Data, bool Changed);
public sealed record RefreshResult(SyncResult Changes, bool Changed);

/// <summary>
/// Owns the active source, queries and their caches. Call from one owner context
/// (the desktop UI context); I/O runs in the background. Null means superseded,
/// cancelled, closed, or no work. Only current-operation errors reach the caller.
/// </summary>
public sealed class MonitoringSession : IDisposable
{
    readonly string _cacheRoot;
    readonly Func<DataStore, double, double, double, CancellationToken, Task<WindowData>> _read;
    readonly Func<DataStore, long, Task<Dictionary<string, Sample[]>>> _readCursor;
    CancellationTokenSource _sourceCancel = new();
    CancellationTokenSource? _loadCancel;
    LogImporter? _importer;
    long _generation, _dataRevision = -1, _cursorRevision = -1, _cursorBucket = long.MinValue;
    Dictionary<string, Sample[]> _cursorSamples = new();
    bool _closed, _scanBusy, _cursorBusy;
    int _opening;
    public DataStore Store
    {
        get; private set;
    }
    public WindowData? Data
    {
        get; private set;
    }

    public MonitoringSession(DataStore store, string cacheRoot) : this(store, cacheRoot,
        (source, left, right, span, token) => Task.Run(() => source.Read(left, right, span, token), token),
        (source, bucket) => Task.Run(() => source.ReadCursorBucket(bucket)))
    {
    }

    internal MonitoringSession(DataStore store, string cacheRoot,
        Func<DataStore, double, double, double, CancellationToken, Task<WindowData>> read,
        Func<DataStore, long, Task<Dictionary<string, Sample[]>>> readCursor)
    {
        Store = store;
        _cacheRoot = cacheRoot;
        _read = read;
        _readCursor = readCursor;
    }

    bool Current(DataStore store, long generation) => !_closed && generation == _generation && ReferenceEquals(store, Store);

    public async Task<SyncResult?> OpenAsync(string root, Action<string>? progress = null, Func<Task>? cachedReady = null)
    {
        if (_closed)
            return null;
        _sourceCancel.Cancel();
        _sourceCancel.Dispose();
        _sourceCancel = new();
        var token = _sourceCancel.Token;
        _opening++;
        try
        {
            var store = await Task.Run(() => LogImporter.OpenDirectory(root, _cacheRoot), token);
            var importer = new LogImporter(store);
            bool published = false;
            void Publish()
            {
                _loadCancel?.Cancel();
                _generation++;
                Store = store;
                _importer = importer;
                Data = null;
                InvalidateDisplay();
                InvalidateCursor();
                published = true;
            }
            if (cachedReady != null && store.HasData && !File.Exists(Path.Combine(store.Folder, "pending-refresh.json")))
            {
                if (_closed || token.IsCancellationRequested)
                    return null;
                Publish();
                await cachedReady();
                if (_closed || token.IsCancellationRequested)
                    return null;
            }
            var guardedProgress = new Progress<string>(message =>
            {
                if (!token.IsCancellationRequested)
                    progress?.Invoke(message);
            });
            var result = await Task.Run(() => importer.Sync(true, guardedProgress, token), token);
            if (_closed || token.IsCancellationRequested)
                return null;
            if (!published)
                Publish();
            return result;
        }
        catch (Exception) when (_closed || token.IsCancellationRequested) { return null; }
        finally { _opening--; }
    }

    public async Task<RefreshResult?> RefreshAsync(bool full)
    {
        if (_closed || _scanBusy || _opening > 0 || _importer == null)
            return null;
        var store = Store;
        var importer = _importer;
        long generation = _generation, revision = store.Revision;
        var token = _sourceCancel.Token;
        _scanBusy = true;
        try
        {
            var result = await Task.Run(() => importer.Sync(full, null, token), token);
            if (!Current(store, generation) || token.IsCancellationRequested)
                return null;
            bool changed = revision != store.Revision;
            if (changed)
                InvalidateCursor();
            return new(result, changed);
        }
        catch (Exception) when (!Current(store, generation) || token.IsCancellationRequested) { return null; }
        finally { _scanBusy = false; }
    }

    public async Task<bool> CompactAsync()
    {
        if (_closed || _scanBusy || _opening > 0 || _importer == null)
            return false;
        var importer = _importer;
        var store = Store;
        long generation = _generation;
        var token = _sourceCancel.Token;
        _scanBusy = true;
        try
        {
            await Task.Run(() => importer.Compact(token), token);
            return Current(store, generation) && !token.IsCancellationRequested;
        }
        catch (Exception) when (!Current(store, generation) || token.IsCancellationRequested) { return false; }
        finally { _scanBusy = false; }
    }
    public bool IsMonitoring => _importer != null;
    public void InvalidateDisplay() => _dataRevision = -1;
    public void InvalidateCursor()
    {
        _cursorBucket = long.MinValue;
        _cursorRevision = -1;
    }

    public async Task<DisplayResult?> LoadAsync(double left, double right, bool force = false)
    {
        if (_closed || !Store.HasData)
            return null;
        // Even a cache hit supersedes an in-flight request for a different view.
        _loadCancel?.Cancel();
        var cancellation = new CancellationTokenSource();
        _loadCancel = cancellation;
        var store = Store;
        long revision = store.Revision, generation = _generation;
        try
        {
            double span = right - left;
            if (!force && _dataRevision == revision && Data != null && Data.Left <= left && Data.Right >= right && Data.Resolution == DataStore.ResolutionFor(span))
                return new(Data, false);
            var data = await _read(store, left - span * .2, right + span * .2, span, cancellation.Token);
            if (cancellation.IsCancellationRequested || !Current(store, generation) || revision != store.Revision)
                return null;
            Data = data;
            _dataRevision = revision;
            return new(data, true);
        }
        catch (Exception) when (cancellation.IsCancellationRequested || !Current(store, generation) || revision != store.Revision) { return null; }
        finally
        {
            if (ReferenceEquals(_loadCancel, cancellation))
                _loadCancel = null;
            cancellation.Dispose();
        }
    }

    public async Task<IReadOnlyDictionary<string, Sample[]>?> ReadCursorAsync(double time)
    {
        if (_closed || !Store.HasData || _cursorBusy)
            return null;
        _cursorBusy = true;
        var store = Store;
        long revision = store.Revision, generation = _generation;
        try
        {
            long bucket = (long)Math.Floor(time / 300);
            if (bucket != _cursorBucket || revision != _cursorRevision)
            {
                var samples = await _readCursor(store, bucket);
                if (!Current(store, generation) || revision != store.Revision)
                    return null;
                _cursorSamples = samples;
                _cursorBucket = bucket;
                _cursorRevision = revision;
            }
            return _cursorSamples;
        }
        catch (Exception) when (!Current(store, generation) || revision != store.Revision) { return null; }
        finally { _cursorBusy = false; }
    }

    public void Dispose()
    {
        if (_closed)
            return;
        _closed = true;
        _sourceCancel.Cancel();
        _sourceCancel.Dispose();
        _loadCancel?.Cancel();
    }

}
