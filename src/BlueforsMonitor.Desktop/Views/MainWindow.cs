using Avalonia;
using Avalonia.Controls;
using static BlueforsAvalonia.ViewElements;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ScottPlot.Avalonia;
using System.Globalization;
using System.Diagnostics;
using SColor = ScottPlot.Color;

namespace BlueforsAvalonia;

public sealed partial class MainWindow : Window
{
    readonly EventListControl _eventList = new();
    readonly DateRangeControl _dateRange = new();
    readonly ChannelPanelControl _channels = new();
    readonly MonitorViewModel _viewModel = new();
    readonly MonitoringSession _session;
    readonly AppSettings _settings = AppSettings.Load();
    readonly AvaPlot _plot = new();
    readonly CursorOverlay _overlay = new();
    readonly EventBand _band = new();
    readonly TextBlock _status = Text("正在载入…", 11, "SecondaryText");
    readonly TextBlock _cursorLabel = Text("移动光标查看原始记录", 12, "CursorText");
    readonly CheckBox _follow = new() { Content = "跟随最新", VerticalAlignment = VerticalAlignment.Center };
    readonly ComboBox _span = new() { ItemsSource = new[] { "1 小时", "6 小时", "24 小时", "7 天", "30 天" }, Width = 92 };
    readonly TextBlock _sourceInfo = Text("", 11, "SecondaryText");
    bool _dark = true, _changingUi;
    readonly double[] _spans = [3600, 21600, 86400, 604800, 2592000];
    readonly DispatcherTimer _scanTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    string? _contextChannel;
    readonly AxisController _axisController;
    string ChannelColor(string key) => UiTheme.Channel(key, _dark);
    readonly DispatcherTimer _loadTimer = new() { Interval = TimeSpan.FromMilliseconds(130) };
    readonly DispatcherTimer _cursorTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    EventRecord? _selected;
    ScottPlot.Plottables.HorizontalSpan? _shade;
    double? _cursorTime, _marker;
    bool _closed, _arrangePending;
    double _cursorRendered = double.NaN;
    string? _dragChannel;
    bool _dragging;
    Point _dragPoint;
    double _dragLeft, _dragRight, _dragMin, _dragMax, _dragFactor;
    public Task? LastLoad
    {
        get; private set;
    }

    public MainWindow(DataStore store)
    {
        _session = new MonitoringSession(store, AppSettings.Root);
        _axisController = new AxisController(_plot.Plot, _dark);
        Title = "Bluefors Monitor · Avalonia 0.4";
        Width = 1460;
        Height = 980;
        MinWidth = 1000;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.Bind(BackgroundProperty, this.GetResourceObservable("Page"));
        FontFamily = new FontFamily(UiTheme.Font);
        DataContext = _viewModel;
        _status.Bind(TextBlock.TextProperty, Avalonia.Data.CompiledBinding.Create((MonitorViewModel model) => model.Status));
        _sourceInfo.Bind(TextBlock.TextProperty, Avalonia.Data.CompiledBinding.Create((MonitorViewModel model) => model.Source));
        _eventList.Bind(EventListControl.CountTextProperty, Avalonia.Data.CompiledBinding.Create((MonitorViewModel model) => model.EventCount));
        BuildUi();
        ConfigurePlot();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        SetTheme(_settings.Theme);
        _loadTimer.Tick += (_, _) => { _loadTimer.Stop(); LastLoad = LoadVisible(); };
        _cursorTimer.Tick += async (_, _) => { UpdateOverlay(); await UpdateReadout(); };
        _scanTimer.Tick += async (_, _) => await SyncCurrent(false);
        _cursorTimer.Start();
        _scanTimer.Start();
        Opened += async (_, _) =>
        {
            UpdateMetadata();
            if (_settings.LogRoot is string root && Directory.Exists(root))
                await OpenLogs(root);
            else if (_session.Store.Snapshot.Events.Length > 0)
                await ShowEvent(_session.Store.Snapshot.Events[0]);
            else if (_session.Store.HasData)
                Navigate(_session.Store.Snapshot.Bounds[1] - 21600, _session.Store.Snapshot.Bounds[1], true);
            else
                _viewModel.Status = "选择 Logfiles 目录，开始建立本机索引。";
        };
        Closed += (_, _) => { _closed = true; _session.Dispose(); _loadTimer.Stop(); _cursorTimer.Stop(); _scanTimer.Stop(); };
    }
    void BuildUi()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto"), Margin = new Thickness(12, 8), RowSpacing = 5 };
        Content = root;
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        void AddTool(Control control)
        {
            control.Margin = new Thickness(0, 0, 7, 0);
            toolbar.Children.Add(control);
        }
        AddTool(Button("选择 Logfiles…", () => _ = ChooseLogs()));
        _span.SelectedIndex = Math.Max(0, Array.IndexOf(_spans, _settings.FollowSeconds));
        _follow.IsChecked = false;
        _follow.IsCheckedChanged += (_, _) =>
        {
            if (_changingUi)
                return;
            _settings.Follow = _follow.IsChecked == true;
            _settings.Save();
            if (_follow.IsChecked == true && _session.Store.HasData)
                FollowLatest(true);
        };
        _span.SelectionChanged += (_, _) =>
        {
            if (_changingUi || _span.SelectedIndex < 0)
                return;
            _settings.FollowSeconds = _spans[_span.SelectedIndex];
            _settings.Save();
            if (_session.Store.HasData)
            {
                double anchor = _follow.IsChecked == true ? _session.Store.Snapshot.Bounds[1] : (_axisController.Left + _axisController.Right) / 2 + _settings.FollowSeconds / 2;
                Navigate(anchor - _settings.FollowSeconds, anchor, true, false);
            }
        };
        AddTool(_follow);
        AddTool(_span);
        AddTool(Button("刷新", () => _ = SyncCurrent(true)));
        AddTool(_dateToggle);
        AddTool(_focusToggle);
        var more = new Button { Content = "更多", Padding = new Thickness(12, 7) };
        var menu = CreateMenu();
        var themeMenu = new MenuItem { Header = "主题" };
        foreach (string mode in new[] { "跟随系统", "浅色", "深色" })
        {
            var item = new MenuItem { Header = mode };
            item.Click += (_, _) => SetTheme(mode);
            themeMenu.Items.Add(item);
        }
        menu.Items.Add(themeMenu);
        var arrange = new MenuItem { Header = "重新排列通道" };
        arrange.Click += (_, _) => Arrange();
        menu.Items.Add(arrange);
        more.ContextMenu = menu;
        more.Click += (_, _) => menu.Open(more);
        AddTool(more);
        Put(root, toolbar, 0, 0);
        var info = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        _sourceInfo.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(_sourceInfo, _viewModel.Source);
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MonitorViewModel.Source))
                ToolTip.SetTip(_sourceInfo, _viewModel.Source);
            if (e.PropertyName == nameof(MonitorViewModel.Status))
                ToolTip.SetTip(_status, _viewModel.Status);
        };
        Put(info, _sourceInfo, 0);
        Put(info, _timeSummary, 1);
        Put(root, info, 0, 1);
        _dateRange.RangeApplied += (start, end) => Navigate(Clock.Unix(start), Clock.Unix(end), true);
        Put(root, _dateRange, 0, 2);
        _channels.Configure(_settings.VisibleChannels, _settings.MultipleAxes);
        _channels.VisibilityChanged += () =>
        {
            _settings.VisibleChannels = _channels.VisibleChannels;
            _settings.Save();
            SyncAxes();
            if (_session.Data != null)
            {
                Draw();
                Arrange(true);
            }
            _cursorRendered = double.NaN;
        };
        _channels.AxisOptionsChanged += () =>
        {
            _settings.MultipleAxes = _channels.MultipleAxes;
            _settings.Save();
            SyncAxes();
            _plot.Refresh();
        };
        Put(root, _channels, 0, 3);
        _workspace.RowDefinitions = new RowDefinitions("*,6,150");
        _workspace.RowDefinitions[0].MinHeight = 240;
        var plotRegion = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto"), RowSpacing = 3 };
        var chart = new Grid { MinHeight = 180, ClipToBounds = true };
        chart.Children.Add(_plot);
        chart.Children.Add(_overlay);
        var border = new Border { Child = chart, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("Border"));
        Put(plotRegion, border, 0, 0);
        _band.IsVisible = _settings.EventBandVisible;
        _band.Events = _session.Store.Snapshot.Events;
        _band.Picked += (item, jump) => { SelectEvent(item); if (jump) LastLoad = ShowEvent(item); };
        Put(plotRegion, _band, 0, 1);
        var readout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _cursorLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        Put(readout, _cursorLabel, 0);
        var marks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        marks.Children.Add(Button("标记时间", () => { _marker = _cursorTime; _cursorRendered = double.NaN; UpdateOverlay(); }));
        marks.Children.Add(Button("清除标记", () => { _marker = null; _cursorRendered = double.NaN; UpdateOverlay(); }));
        Put(readout, marks, 1);
        Put(plotRegion, readout, 0, 2);
        Put(_workspace, plotRegion, 0, 0);
        Put(_workspace, _splitter, 0, 1);
        _eventList.SelectionRequested += SelectEvent;
        _eventList.ReplayRequested += item => LastLoad = ShowEvent(item);
        Put(_workspace, _eventList, 0, 2);
        Put(root, _workspace, 0, 4);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _status.TextTrimming = TextTrimming.CharacterEllipsis;
        Put(footer, _status, 0);
        var help = Text("操作帮助 · UTC+08:00", 10, "SecondaryText");
        ToolTip.SetTip(help, "图内拖动 / 滚轮：平移 / 缩放时间\n纵轴拖动 / 滚轮：仅调整该通道\n事件单击高亮、双击回看；右键查看更多操作");
        Put(footer, help, 1);
        Put(root, footer, 0, 5);
        InitializeLayout();
    }
    void ConfigurePlot()
    {
        _plot.UserInputProcessor.Disable();
        var plot = _plot.Plot;
        plot.FigureBackground.Color = SColor.FromHex(UiTheme.Hex("Plot", _dark));
        plot.DataBackground.Color = SColor.FromHex(UiTheme.Hex("Plot", _dark));
        plot.Font.Set(UiTheme.Font);
        plot.Axes.Color(SColor.FromHex(UiTheme.Hex("Axis", _dark)));
        plot.Axes.Left.IsVisible = false;
        plot.Axes.Right.IsVisible = false;
        plot.Axes.Top.IsVisible = false;
        _axisController.SetDateAxis(_dark);
        plot.Grid.MajorLineColor = SColor.FromHex(UiTheme.Hex("MajorGrid", _dark));
        plot.Grid.MinorLineColor = SColor.FromHex(UiTheme.Hex("MinorGrid", _dark));
        plot.Grid.YAxis = plot.Axes.Left;
        plot.Axes.SetLimitsY(0, 1);
        _plot.AddHandler(PointerPressedEvent, PlotPressed, RoutingStrategies.Tunnel);
        _plot.AddHandler(PointerMovedEvent, PlotMoved, RoutingStrategies.Tunnel);
        _plot.AddHandler(PointerReleasedEvent, PlotReleased, RoutingStrategies.Tunnel);
        _plot.AddHandler(PointerWheelChangedEvent, PlotWheel, RoutingStrategies.Tunnel);
        _plot.ContextMenu = CreateMenu();
        _plot.PointerCaptureLost += (_, _) => { _dragging = false; _dragChannel = null; };
    }
    void Navigate(double left, double right, bool arrange = false, bool stopFollow = true)
    {
        if (stopFollow)
            StopFollowing();
        SetRange(left, right);
        _arrangePending |= arrange;
        _dateRange.SetRange(_axisController.Left, _axisController.Right);
        LastLoad = LoadVisible();
    }
    void SetRange(double left, double right)
    {
        _axisController.SetRange(left, right, _session.Store.Snapshot.Bounds[1] - _session.Store.Snapshot.Bounds[0]);
        _eventList.SetRange(_axisController.Left, _axisController.Right);
        _timeSummary.Text = $"{Clock.At(_axisController.Left):MM-dd HH:mm} — {Clock.At(_axisController.Right):MM-dd HH:mm}";
        _plot.Refresh();
        UpdateOverlay();
    }
    async Task LoadVisible(bool force = false)
    {
        if (!_session.Store.HasData)
            return;
        try
        {
            var request = _session.LoadAsync(_axisController.Left, _axisController.Right, force);
            if (!request.IsCompleted)
                _viewModel.Status = "正在读取当前时间范围…";
            var result = await request;
            if (result == null)
                return;
            var drawTimer = Stopwatch.StartNew();
            bool unitsChanged = _axisController.UpdateUnits(result.Data, _channels.VisibleChannels);
            if (result.Changed || unitsChanged)
                Draw();
            if (_arrangePending)
            {
                Arrange();
                _arrangePending = false;
            }
            var data = result.Data;
            _viewModel.Status = $"{data.Count:N0} 个绘图点 · {(data.Resolution == 0 ? "原始样本" : $"{data.Resolution / 60} 分钟极值概览")} · 读取 {data.Milliseconds:F0} ms / 准备绘图 {drawTimer.Elapsed.TotalMilliseconds:F0} ms · 光标查原始记录 · 共 {_session.Store.Snapshot.Counts.Values.Sum():N0} 条";
            if (result.Changed)
                _cursorRendered = double.NaN;
        }
        catch (Exception ex) { _viewModel.Status = "读取失败：" + ex.Message; Console.Error.WriteLine(ex); }
    }
    void Draw()
    {
        if (_session.Data is null)
            return;
        _axisController.UpdateUnits(_session.Data, _channels.VisibleChannels);
        _cursorRendered = double.NaN;
        SyncAxes();
        _shade = PlotController.Render(_plot.Plot, _session.Data, _axisController.Axes,
            _channels.VisibleChannels.ToHashSet(), ChannelColor, _axisController.Factor, _dark);
        UpdateSelection();
        _plot.Refresh();
    }
    void Arrange(bool keepManual = false)
    {
        _axisController.Arrange(_session.Data, _channels.VisibleChannels, keepManual);
        UpdateModes();
        _plot.Refresh();
    }
    void UpdateModes() => _channels.SetManualChannels(_axisController.ManualChannels);
    void SelectEvent(EventRecord item)
    {
        _selected = item;
        _eventList.Select(item);
        UpdateSelection();
    }
    void UpdateSelection()
    {
        _band.Selected = _selected;
        _band.InvalidateVisual();
        if (_shade is not null)
        {
            _shade.IsVisible = _selected != null;
            if (_selected != null)
            {
                _shade.X1 = Clock.Plot(_selected.Start);
                _shade.X2 = Clock.Plot(_selected.End);
            }
        }
        _plot.Refresh();
    }
    async Task ShowEvent(EventRecord item)
    {
        StopFollowing();
        SelectEvent(item);
        _arrangePending = true;
        _cursorTime = item.Peak;
        _cursorRendered = double.NaN;
        SetRange(item.Start - 1800, item.End + 1800);
        if (_dateRange.Start.Value is null)
            _dateRange.SetRange(_axisController.Left, _axisController.Right);
        LastLoad = LoadVisible();
        await LastLoad;
    }
    Rect DataRect()
    {
        var rect = _plot.Plot.LastRender.DataRect;
        double scale = _plot.DisplayScale;
        return new Rect(rect.Left / scale, rect.Top / scale, Math.Max(0, rect.Width / scale), Math.Max(0, rect.Height / scale));
    }
    string? AxisAt(Point point) => _axisController.AxisAt(new ScottPlot.Pixel((float)(point.X * _plot.DisplayScale), (float)(point.Y * _plot.DisplayScale)));
    void PlotPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(_plot).Properties.IsRightButtonPressed)
        {
            _contextChannel = AxisAt(e.GetPosition(_plot));
            return;
        }
        if (!e.GetCurrentPoint(_plot).Properties.IsLeftButtonPressed)
            return;
        var point = e.GetPosition(_plot);
        var rect = DataRect();
        var key = AxisAt(point);
        if (!rect.Contains(point) && key == null)
            return;
        _dragging = true;
        _dragChannel = key;
        _dragPoint = point;
        _dragLeft = _axisController.Left;
        _dragRight = _axisController.Right;
        if (key != null)
        {
            _channels.SelectedAxis = key;
            _dragFactor = _axisController.Factor(key);
            _dragMin = _axisController.Axes[key].Min;
            _dragMax = _axisController.Axes[key].Max;
        }
        e.Pointer.Capture(_plot);
        e.Handled = true;
    }
    void PlotMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(_plot);
        var rect = DataRect();
        if (rect.Width <= 0)
            return;
        if (_dragging)
        {
            if (_dragChannel is string key)
            {
                double scale = _axisController.Factor(key) / _dragFactor;
                _axisController.Pan(key, _dragMin * scale, _dragMax * scale, (point.Y - _dragPoint.Y) / rect.Height);
                UpdateModes();
                _plot.Refresh();
            }
            else
            {
                StopFollowing();
                double shift = -(point.X - _dragPoint.X) / rect.Width * (_dragRight - _dragLeft);
                SetRange(_dragLeft + shift, _dragRight + shift);
                QueueLoad();
            }
            e.Handled = true;
        }
        if (rect.Contains(point))
        {
            _cursorTime = _axisController.Left + (point.X - rect.Left) / rect.Width * (_axisController.Right - _axisController.Left);
            UpdateOverlay();
        }
    }
    void PlotReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return;
        bool time = _dragChannel == null;
        _dragging = false;
        _dragChannel = null;
        e.Pointer.Capture(null);
        if (time)
            QueueLoad();
        e.Handled = true;
    }
    void PlotWheel(object? sender, PointerWheelEventArgs e)
    {
        var point = e.GetPosition(_plot);
        var rect = DataRect();
        string? key = AxisAt(point);
        double delta = Math.Abs(e.Delta.Y) > .001 ? e.Delta.Y : e.Delta.X;
        double factor = Math.Exp(-Math.Clamp(delta, -8, 8) * .14);
        if (key != null)
        {
            var axis = _axisController.Axes[key];
            double anchor = axis.Max - (point.Y - rect.Top) / rect.Height * (axis.Max - axis.Min);
            ZoomChannel(key, factor, anchor);
        }
        else if (rect.Contains(point))
        {
            StopFollowing();
            double anchor = _axisController.Left + (point.X - rect.Left) / rect.Width * (_axisController.Right - _axisController.Left);
            SetRange(anchor + (_axisController.Left - anchor) * factor, anchor + (_axisController.Right - anchor) * factor);
            QueueLoad();
        }
        e.Handled = true;
    }
    void ZoomChannel(string key, double factor, double anchor)
    {
        _axisController.Zoom(key, factor, anchor);
        UpdateModes();
        _plot.Refresh();
    }
    void QueueLoad()
    {
        _loadTimer.Stop();
        _loadTimer.Start();
    }
    void UpdateOverlay()
    {
        var rect = DataRect();
        if (rect.Width <= 0 || _axisController.Right <= _axisController.Left)
            return;
        double X(double t) => rect.Left + (t - _axisController.Left) / (_axisController.Right - _axisController.Left) * rect.Width;
        double? cursor = _cursorTime.HasValue ? X(_cursorTime.Value) : null;
        double? marker = _marker.HasValue ? X(_marker.Value) : null;
        if (_overlay.DataRect != rect || _overlay.CursorX != cursor || _overlay.MarkerX != marker)
        {
            _overlay.DataRect = rect;
            _overlay.CursorX = cursor;
            _overlay.MarkerX = marker;
            _overlay.InvalidateVisual();
        }
        if (_band.Left != _axisController.Left || _band.Right != _axisController.Right || _band.PlotLeft != rect.Left + 1 || _band.PlotRight != rect.Right + 1)
        {
            _band.Left = _axisController.Left;
            _band.Right = _axisController.Right;
            _band.PlotLeft = rect.Left + 1;
            _band.PlotRight = rect.Right + 1;
            _band.InvalidateVisual();
        }
    }
    async Task UpdateReadout()
    {
        if (_cursorTime is not double time || _cursorRendered == time)
            return;
        try
        {
            var samples = await _session.ReadCursorAsync(time);
            if (samples == null)
                return;
            foreach (var key in DataStore.Keys)
            {
                var sample = DataStore.Nearest(samples.GetValueOrDefault(key, []), time);
                string text = !_channels.IsChannelVisible(key) ? "已隐藏" : sample == null ? "无记录" : (sample.V * _axisController.Factor(key)).ToString(_axisController.Unit(key) == "mK" ? "F3" : "G6", CultureInfo.InvariantCulture) + " " + _axisController.Unit(key);
                string tooltip = sample == null ? "光标前后 90 秒内没有原始记录" : $"原始样本 {Clock.At(sample.T):yyyy-MM-dd HH:mm:ss} · 与光标相差 {sample.T - time:+0.0;-0.0;0} 秒";
                _channels.SetReadout(key, text, tooltip);
            }
            _cursorLabel.Text = $"{Clock.At(time):yyyy-MM-dd HH:mm:ss}   ·   原始读数" + (_marker.HasValue ? $"   ·   距标记 {(time - _marker.Value) / 60:+0.00;-0.00;0} min" : "");
            _cursorRendered = time;
        }
        catch (Exception ex) { _cursorLabel.Text = "读数读取失败：" + ex.Message; }
    }
}
