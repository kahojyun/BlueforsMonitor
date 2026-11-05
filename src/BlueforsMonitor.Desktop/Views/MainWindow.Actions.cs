using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;

namespace BlueforsMonitor;

public sealed partial class MainWindow
{
    void SetTheme(string mode)
    {
        RequestedThemeVariant = mode == "浅色" ? ThemeVariant.Light : mode == "跟随系统" ? ThemeVariant.Default : ThemeVariant.Dark;
        _settings.Theme = mode;
        _settings.Save();
        ApplyPalette();
    }
    void ApplyPalette()
    {
        _dark = ActualThemeVariant != ThemeVariant.Light;
        var plot = _plot.Plot;
        plot.FigureBackground.Color = ScottPlot.Color.FromHex(UiTheme.Hex("Plot", _dark));
        plot.DataBackground.Color = plot.FigureBackground.Color;
        plot.Axes.Color(ScottPlot.Color.FromHex(UiTheme.Hex("Axis", _dark)));
        plot.Grid.MajorLineColor = ScottPlot.Color.FromHex(UiTheme.Hex("MajorGrid", _dark));
        plot.Grid.MinorLineColor = ScottPlot.Color.FromHex(UiTheme.Hex("MinorGrid", _dark));
        _axisController.ApplyPalette(_dark);
        _band.Dark = _dark;
        _band.InvalidateVisual();
        _overlay.Dark = _dark;
        _overlay.InvalidateVisual();
        if (_session.Data != null)
            Draw();
        else
            _plot.Refresh();
    }
    void SyncAxes() => _axisController.Sync(_channels.VisibleChannels, _settings.MultipleAxes, _channels.SelectedAxis);
    void StopFollowing()
    {
        if (_follow.IsChecked != true)
            return;
        _changingUi = true;
        _follow.IsChecked = false;
        _changingUi = false;
        _settings.Follow = false;
        _settings.Save();
    }
    void FollowLatest(bool arrange)
    {
        if (!_session.Store.HasData)
            return;
        _arrangePending |= arrange;
        double right = _session.Store.Snapshot.Bounds[1];
        SetRange(right - _settings.FollowSeconds, right);
        LastLoad = LoadVisible(true);
    }
    void ExpandAutomaticAxes()
    {
        if (!_settings.AutomaticAxes)
            return;
        _axisController.ExpandAutomatic(_session.Data, _channels.VisibleChannels);
        _plot.Refresh();
    }
    void UpdateMetadata()
    {
        var data = _session.Store.Snapshot;
        _viewModel.Source = _session.Store.HasData
            ? $"{(_session.Store.IsLive ? _session.Store.LogRoot : "历史缓存")}   ·   {Clock.At(data.Bounds[0]):yyyy.MM.dd} — {Clock.At(data.Bounds[1]):yyyy.MM.dd HH:mm}   ·   {data.Counts.Values.Sum():N0} 条" + (_session.Store.IsLive ? $"   ·   最新记录距今 {Math.Max(0, (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - data.Bounds[1]) / 60):F0} 分钟" : "")
            : (_session.Store.LogRoot ?? "选择 Logfiles 日志目录开始使用。");
        double? selected = _selected?.Start;
        _eventList.SetEvents(data.Events);
        _band.Events = data.Events;
        _viewModel.EventCount = $"升温事件  {data.Events.Length}";
        _selected = selected.HasValue ? data.Events.FirstOrDefault(e => e.Start == selected.Value) : null;
        if (_selected != null)
            SelectEvent(_selected);
        else
        {
            _eventList.Select(null);
            UpdateSelection();
        }
    }
    async Task ChooseLogs()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择 Bluefors Logfiles 目录", AllowMultiple = false });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is string root)
            await OpenLogs(root);
    }
    async Task OpenLogs(string root)
    {
        _viewModel.Status = "正在索引日志，后台运行中…";
        try
        {
            bool cachedShown = false;
            var result = await _session.OpenAsync(root, message => _viewModel.Status = message, async () =>
            {
                cachedShown = true;
                var cachedStore = _session.Store;
                await ShowOpenedSource(root);
                if (_closed || !ReferenceEquals(cachedStore, _session.Store))
                    return;
                _viewModel.Status += " · 已显示缓存，后台检查日志…";
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            });
            if (result == null)
                return;
            var store = _session.Store;
            if (!cachedShown)
                await ShowOpenedSource(root);
            else
            {
                UpdateMetadata();
                if (double.IsFinite(result.DirtyFrom))
                {
                    if (_follow.IsChecked == true)
                        FollowLatest(false);
                    else
                        LastLoad = LoadVisible(true);
                    if (LastLoad != null)
                        await LastLoad;
                    if (_closed || !ReferenceEquals(store, _session.Store))
                        return;
                    ExpandAutomaticAxes();
                }
                else
                    _viewModel.Status = "缓存已显示，日志检查完成，无新有效记录。";
            }
            if (_closed || !ReferenceEquals(store, _session.Store))
                return;
            if (result.Skipped > 0)
                _viewModel.Status += $" · 跳过 {result.Skipped} 行或文件";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _viewModel.Status = "无法打开日志目录：" + ex.Message; Console.Error.WriteLine(ex); }
    }
    async Task ShowOpenedSource(string root)
    {
        var store = _session.Store;
        ResetSourceView();
        _settings.LogRoot = root;
        _settings.Save();
        _changingUi = true;
        _follow.IsChecked = _settings.Follow;
        _changingUi = false;
        if (store.HasData)
        {
            Navigate(store.Snapshot.Bounds[1] - _settings.FollowSeconds, store.Snapshot.Bounds[1], true, false);
            if (LastLoad != null)
                await LastLoad;
        }
        else
            _viewModel.Status = "未找到有效温度、流量或压力记录；将继续每 10 秒检查目录。";
    }
    async Task CompactIndex()
    {
        _viewModel.Status = "正在压缩本机索引；需要临时磁盘空间，日志源文件不变…";
        try
        {
            bool done = await _session.CompactAsync();
            if (!_closed)
                _viewModel.Status = done ? "本机索引压缩完成。" : "当前正在索引或更新，请完成后再压缩。";
        }
        catch (Exception ex) { _viewModel.Status = "索引压缩未完成，可稍后重试：" + ex.Message; }
    }
    void ResetSourceView()
    {
        var store = _session.Store;
        _loadTimer.Stop();
        _axisController.ResetUnitHistory();
        _selected = null;
        _arrangePending = true;
        _cursorRendered = double.NaN;
        _marker = null;
        _cursorTime = store.HasData ? store.Snapshot.Bounds[1] : null;
        _plot.Plot.Clear();
        _shade = null;
        _plot.Refresh();
        UpdateMetadata();
    }
    async Task SyncCurrent(bool full)
    {
        if (_closed)
            return;
        if (!_session.IsMonitoring)
        {
            if (full)
            {
                _session.InvalidateCursor();
                await LoadVisible(true);
            }
            return;
        }
        try
        {
            var refresh = await _session.RefreshAsync(full);
            if (refresh == null)
                return;
            var result = refresh.Changes;
            var store = _session.Store;
            if (!refresh.Changed)
            {
                if (full)
                    _viewModel.Status = $"没有新有效记录，当前视野与纵轴保持不变。跳过 {result.Skipped} 行或文件。";
                return;
            }
            _cursorRendered = double.NaN;
            UpdateMetadata();
            if (_follow.IsChecked == true && store.HasData)
            {
                SetRange(store.Snapshot.Bounds[1] - _settings.FollowSeconds, store.Snapshot.Bounds[1]);
                await LoadVisible(true);
                if (_closed || !ReferenceEquals(store, _session.Store))
                    return;
                ExpandAutomaticAxes();
            }
            else if (result.DirtyTo >= _axisController.Left && result.DirtyFrom <= _axisController.Right)
                await LoadVisible(true);
            else
                _session.InvalidateDisplay(); // Keep the plot usable; a later pan must reload.
            if (_closed || !ReferenceEquals(store, _session.Store))
                return;
            if (result.Skipped > 0)
                _viewModel.Status += $" · 跳过 {result.Skipped} 行或文件";
        }
        catch (Exception ex) { _viewModel.Status = "日志更新失败，将在下次轮询重试：" + ex.Message; }
    }
    ContextMenu CreateMenu()
    {
        var menu = new ContextMenu();
        void Item(string title, Action action)
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Item("重排全部曲线（R）", () => Arrange());
        Item("适配选中纵轴（A）", () => FitSelectedAxis());
        Item("压缩本机索引…", () => _ = CompactIndex());
        Item("显示 / 隐藏事件带", () => { _band.IsVisible = !_band.IsVisible; _settings.EventBandVisible = _band.IsVisible; _settings.Save(); });
        Item("查看全部日志时间范围", () => { if (_session.Store.HasData) Navigate(_session.Store.Snapshot.Bounds[0], _session.Store.Snapshot.Bounds[1], true); });
        Item("标记当前光标", () => { _marker = _cursorTime; _cursorRendered = double.NaN; UpdateOverlay(); });
        Item("清除标记", () => { _marker = null; _cursorRendered = double.NaN; UpdateOverlay(); });
        Item("回看高亮事件", () => { if (_selected != null) LastLoad = ShowEvent(_selected); });
        Item("恢复当前纵轴自动适配", () =>
        {
            _axisController.RestoreAutomatic(_session.Data, _channels.VisibleChannels, _contextChannel);
            _plot.Refresh();
            UpdateModes();
        });
        Item("导出曲线 PNG…", () => _ = ExportPng());
        return menu;
    }
    void FitSelectedAxis()
    {
        _axisController.Fit(_session.Data, _channels.SelectedAxis);
        UpdateModes();
        _plot.Refresh();
    }
    async Task ExportPng()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "导出当前曲线", SuggestedFileName = "Bluefors.png", DefaultExtension = "png", FileTypeChoices = [new("PNG 图片") { Patterns = ["*.png"] }] });
        if (file?.TryGetLocalPath() is not string path)
            return;
        try
        {
            _plot.Plot.SavePng(path, Math.Max(1200, (int)_plot.Bounds.Width * 2), Math.Max(650, (int)_plot.Bounds.Height * 2));
            _plot.Refresh();
            _viewModel.Status = "曲线已导出：" + path;
        }
        catch (Exception ex) { _viewModel.Status = "导出失败：" + ex.Message; }
    }
}
