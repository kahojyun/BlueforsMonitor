using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;

namespace BlueforsAvalonia;

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
        plot.FigureBackground.Color = ScottPlot.Color.FromHex(_dark ? "#101c2a" : "#ffffff");
        plot.DataBackground.Color = plot.FigureBackground.Color;
        plot.Axes.Color(ScottPlot.Color.FromHex(_dark ? "#91a5ba" : "#526a7d"));
        plot.Grid.MajorLineColor = ScottPlot.Color.FromHex(_dark ? "#243446" : "#e3eaf0");
        plot.Grid.MinorLineColor = ScottPlot.Color.FromHex(_dark ? "#182638" : "#f0f4f7");
        foreach (var pair in _axes)
            pair.Value.Color(ScottPlot.Color.FromHex(ChannelColor(pair.Key)));
        _band.Dark = _dark;
        _band.InvalidateVisual();
        _overlay.Dark = _dark;
        _overlay.InvalidateVisual();
        if (_data != null)
            Draw();
        else
            _plot.Refresh();
    }
    void SyncCards()
    {
        var visible = DataStore.Keys.Where(k => _checks.TryGetValue(k, out var c) && c.IsChecked == true).ToArray();
        _cards.Columns = Math.Max(1, visible.Length);
        foreach (var pair in _cardByKey)
            pair.Value.IsVisible = visible.Contains(pair.Key);
    }
    void SyncAxes()
    {
        // Hidden axes must leave the layout, not just stop painting their ticks.
        foreach (var axis in _axes.Values)
            _plot.Plot.Axes.Remove(axis);
        foreach (var key in DataStore.Keys.Where(k => _checks[k].IsChecked == true))
        {
            _axes[key].IsVisible = _settings.MultipleAxes || key == (_axisSelector.SelectedItem as string);
            _axes[key].MinimumSize = _axes[key].IsVisible ? (key == "P2" ? 80 : 65) : 0;
            _plot.Plot.Axes.AddRightAxis(_axes[key]);
        }
    }
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
        if (!_store.HasData)
            return;
        _arrangePending |= arrange;
        double right = _store.Snapshot.Bounds[1];
        SetRange(right - _settings.FollowSeconds, right);
        LastLoad = LoadVisible(true);
    }
    void ExpandAutomaticAxes()
    {
        if (_data == null)
            return;
        var keys = DataStore.Keys.Where(k => _checks[k].IsChecked == true).ToArray();
        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            if (_manual.Contains(key))
                continue;
            var values = _data.Series[key].Points.Where(p => p.T >= _left && p.T <= _right).Select(p => p.V * Factor(key)).ToArray();
            if (values.Length == 0)
                continue;
            var axis = _axes[key];
            double height = axis.Max - axis.Min;
            double low = axis.Min + (1 - (i + .94) / keys.Length) * height, high = axis.Min + (1 - (i + .06) / keys.Length) * height;
            double min = values.Min(), max = values.Max();
            if (min >= low && max <= high)
                continue;
            double total = Math.Max(height, (max - min) * keys.Length * 1.45);
            double center = (max + min) / 2, position = 1 - (i + .5) / keys.Length;
            _plot.Plot.Axes.SetLimitsY(center - position * total, center + (1 - position) * total, axis);
        }
        _plot.Refresh();
    }
    void UpdateMetadata()
    {
        var data = _store.Snapshot;
        _viewModel.Source = _store.HasData
            ? $"{(_store.IsLive ? _store.LogRoot : "历史缓存")}   ·   {Clock.At(data.Bounds[0]):yyyy.MM.dd} — {Clock.At(data.Bounds[1]):yyyy.MM.dd HH:mm}   ·   {data.Counts.Values.Sum():N0} 条" + (_store.IsLive ? $"   ·   最新记录距今 {Math.Max(0, (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - data.Bounds[1]) / 60):F0} 分钟" : "")
            : (_store.LogRoot ?? "选择 Logfiles 日志目录开始使用。");
        double? selected = _selected?.Start;
        _events.ItemsSource = data.Events;
        _band.Events = data.Events;
        _viewModel.EventCount = $"升温事件  {data.Events.Length}";
        _selected = selected.HasValue ? data.Events.FirstOrDefault(e => e.Start == selected.Value) : null;
        if (_selected != null)
            SelectEvent(_selected);
        else
        {
            _eventDetail.Text = "点击事件查看同期 Still、流量和压力变化。";
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
        _sourceCancel.Cancel();
        _sourceCancel = new();
        var token = _sourceCancel.Token;
        _viewModel.Status = "正在索引日志，后台运行中…";
        try
        {
            var store = await Task.Run(() => LogImporter.OpenDirectory(root, AppSettings.Root), token);
            var importer = new LogImporter(store);
            var progress = new Progress<string>(message => { if (!token.IsCancellationRequested) _viewModel.Status = message; });
            var result = await Task.Run(() => importer.Sync(true, progress, token), token);
            if (token.IsCancellationRequested || _closed)
                return;
            SwitchStore(store);
            _importer = importer;
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
            if (result.Skipped > 0)
                _viewModel.Status += $" · 跳过 {result.Skipped} 行或文件";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _viewModel.Status = "无法打开日志目录：" + ex.Message; }
    }
    void SwitchStore(DataStore store)
    {
        _loadCancel?.Cancel();
        _loadTimer.Stop();
        _store = store;
        _data = null;
        _dataRevision = -1;
        _selected = null;
        _arrangePending = true;
        _cursorBucket = long.MinValue;
        _cursorRendered = double.NaN;
        _cursorSamples.Clear();
        _marker = null;
        _cursorTime = store.HasData ? store.Snapshot.Bounds[1] : null;
        _plot.Plot.Clear();
        _shade = null;
        _plot.Refresh();
        UpdateMetadata();
    }
    async Task SyncCurrent(bool full)
    {
        if (_scanBusy || _closed)
            return;
        var store = _store;
        var importer = _importer;
        if (importer == null)
        {
            if (full)
            {
                _cursorBucket = long.MinValue;
                await LoadVisible(true);
            }
            return;
        }
        _scanBusy = true;
        var token = _sourceCancel.Token;
        try
        {
            long revision = store.Revision;
            var result = await Task.Run(() => importer.Sync(full, null, token), token);
            if (_closed || token.IsCancellationRequested || !ReferenceEquals(store, _store))
                return;
            if (revision == store.Revision)
            {
                if (full)
                    _viewModel.Status = $"没有新有效记录，当前视野与纵轴保持不变。跳过 {result.Skipped} 行或文件。";
                return;
            }
            _cursorBucket = long.MinValue;
            _cursorRendered = double.NaN;
            UpdateMetadata();
            if (_follow.IsChecked == true && store.HasData)
            {
                SetRange(store.Snapshot.Bounds[1] - _settings.FollowSeconds, store.Snapshot.Bounds[1]);
                await LoadVisible(true);
                ExpandAutomaticAxes();
            }
            else if (result.DirtyTo >= _left && result.DirtyFrom <= _right)
                await LoadVisible(true);
            else
                _dataRevision = -1; // Keep the current plot usable; a later pan must reload.
            if (result.Skipped > 0)
                _viewModel.Status += $" · 跳过 {result.Skipped} 行或文件";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (ReferenceEquals(store, _store)) _viewModel.Status = "日志更新失败，将在下次轮询重试：" + ex.Message; }
        finally { _scanBusy = false; }
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
        Item("适配选中纵轴", () => FitSelectedAxis());
        Item("显示 / 隐藏事件带", () => { _band.IsVisible = !_band.IsVisible; _settings.EventBandVisible = _band.IsVisible; _settings.Save(); });
        Item("查看全部日志时间范围", () => { if (_store.HasData) Navigate(_store.Snapshot.Bounds[0], _store.Snapshot.Bounds[1], true); });
        Item("标记当前光标", () => { _marker = _cursorTime; _cursorRendered = double.NaN; UpdateOverlay(); });
        Item("清除标记", () => { _marker = null; _cursorRendered = double.NaN; UpdateOverlay(); });
        Item("回看高亮事件", () => { if (_selected != null) LastLoad = ShowEvent(_selected); });
        Item("恢复当前纵轴自动适配", () =>
        {
            if (_contextChannel is not string key)
            {
                Arrange();
                return;
            }
            var keep = new HashSet<string>(_manual);
            _manual.Clear();
            foreach (string k in DataStore.Keys.Where(k => k != key))
                _manual.Add(k);
            Arrange(true);
            _manual.Clear();
            foreach (var k in keep.Where(k => k != key))
                _manual.Add(k);
            UpdateModes();
        });
        Item("导出曲线 PNG…", () => _ = ExportPng());
        return menu;
    }
    void FitSelectedAxis()
    {
        if (_data == null || _axisSelector.SelectedItem is not string key)
            return;
        var points = _data.Series[key].Points.Where(p => p.T >= _left && p.T <= _right).Select(p => p.V * Factor(key)).ToArray();
        if (points.Length == 0)
            return;
        double min = points.Min(), max = points.Max(), pad = Math.Max(max - min, Math.Max(Math.Abs(min) * .0002, .000001)) * .1;
        _plot.Plot.Axes.SetLimitsY(min - pad, max + pad, _axes[key]);
        _manual.Remove(key);
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
