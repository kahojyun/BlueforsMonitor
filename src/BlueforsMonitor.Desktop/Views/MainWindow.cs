using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.Platform.Storage;
using ScottPlot.Avalonia;
using System.Globalization;
using System.Diagnostics;
using SColor = ScottPlot.Color;

namespace BlueforsAvalonia;

public sealed partial class MainWindow : Window
{
    readonly ComboBox _axisSelector = new() { Width = 90, ItemsSource = DataStore.Keys, SelectedIndex = 0 };
    readonly MonitorViewModel _viewModel = new();
    DataStore _store;
    readonly AppSettings _settings = AppSettings.Load();
    readonly AvaPlot _plot = new();
    readonly CursorOverlay _overlay = new();
    readonly EventBand _band = new();
    readonly ListBox _events = new();
    readonly TextBlock _status = Text("正在载入…", 11, "#97acc2");
    readonly TextBlock _cursorLabel = Text("移动光标查看原始记录", 12, "#c0d0e0");
    readonly TextBlock _eventDetail = Text("", 11, "#97acc2");
    readonly TextBlock _rangeLabel = Text("", 11, "#97acc2");
    readonly DateTimeField _from = new();
    readonly DateTimeField _to = new();
    readonly ComboBox _theme = new() { ItemsSource = new[] { "深色", "浅色", "跟随系统" }, Width = 110 };
    readonly CheckBox _follow = new() { Content = "跟随最新", VerticalAlignment = VerticalAlignment.Center };
    readonly ComboBox _span = new() { ItemsSource = new[] { "1 小时", "6 小时", "24 小时", "7 天", "30 天" }, Width = 92 };
    readonly TextBlock _sourceInfo = Text("", 11, "#849ab2");
    readonly TextBlock _eventCount = Text("", 14);
    readonly UniformGrid _cards = new() { Rows = 1 };
    readonly Dictionary<string, Border> _cardByKey = new();
    bool _dark = true, _changingUi;
    readonly double[] _spans = [3600, 21600, 86400, 604800, 2592000];
    readonly DispatcherTimer _scanTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    CancellationTokenSource _sourceCancel = new();
    LogImporter? _importer;
    bool _scanBusy;
    string? _contextChannel;
    readonly Dictionary<string, CheckBox> _checks = new();
    readonly Dictionary<string, TextBlock> _values = new();
    readonly Dictionary<string, TextBlock> _modes = new();
    readonly Dictionary<string, ScottPlot.AxisPanels.RightAxis> _axes = new();
    readonly HashSet<string> _manual = [];
    string ChannelColor(string key) => UiTheme.Channel(key, _dark);
    readonly DispatcherTimer _loadTimer = new() { Interval = TimeSpan.FromMilliseconds(130) };
    readonly DispatcherTimer _cursorTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    CancellationTokenSource? _loadCancel;
    WindowData? _data;
    long _dataRevision = -1;
    EventRecord? _selected;
    ScottPlot.Plottables.HorizontalSpan? _shade;
    double _left, _right;
    double? _cursorTime, _marker;
    long _cursorBucket = long.MinValue;
    Dictionary<string, Sample[]> _cursorSamples = new();
    bool _cursorBusy, _closed, _arrangePending;
    double _cursorRendered = double.NaN;
    string? _dragChannel;
    bool _dragging;
    Point _dragPoint;
    double _dragLeft, _dragRight, _dragMin, _dragMax;
    public Task? LastLoad
    {
        get; private set;
    }

    static TextBlock Text(string text, double size = 12, string color = "#e0e9f3")
    {
        var label = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        UiTheme.Foreground(label, color);
        return label;
    }
    static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 7) };
        button.Click += (_, _) => action();
        return button;
    }
    static void Put(Grid grid, Control child, int column, int row = 0)
    {
        Grid.SetColumn(child, column);
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }
    public MainWindow(DataStore store)
    {
        _store = store;
        Title = "Bluefors Monitor · Avalonia 0.4";
        Width = 1460;
        Height = 980;
        MinWidth = 1280;
        MinHeight = 820;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.Bind(BackgroundProperty, this.GetResourceObservable("Page"));
        FontFamily = new FontFamily(UiTheme.Font);
        DataContext = _viewModel;
        _status.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(MonitorViewModel.Status)));
        _sourceInfo.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(MonitorViewModel.Source)));
        _eventCount.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(MonitorViewModel.EventCount)));
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
            else if (_store.Snapshot.Events.Length > 0)
                await ShowEvent(_store.Snapshot.Events[0]);
            else if (_store.HasData)
                Navigate(_store.Snapshot.Bounds[1] - 21600, _store.Snapshot.Bounds[1], true);
            else
                _viewModel.Status = "选择 Logfiles 目录，开始建立本机索引。";
        };
        Closed += (_, _) => { _closed = true; _loadCancel?.Cancel(); _sourceCancel.Cancel(); _loadTimer.Stop(); _cursorTimer.Stop(); _scanTimer.Stop(); };
    }
    void BuildUi()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto,Auto,170,Auto"), Margin = new Thickness(22, 17, 22, 12), RowSpacing = 9 };
        Content = root;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 15 };
        var title = new StackPanel { Spacing = 5 };
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        var heading = Text("低温事件观察", 23, "#edf4fa");
        heading.FontWeight = FontWeight.SemiBold;
        titleLine.Children.Add(heading);
        titleLine.Children.Add(Text("BLUEFORS  /  0.4", 11, "#6c94b5"));
        title.Children.Add(titleLine);
        _sourceInfo.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Children.Add(_sourceInfo);
        Put(header, title, 0);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(Button("选择 Logfiles…", () => _ = ChooseLogs()));
        _theme.SelectedItem = _settings.Theme;
        _theme.SelectionChanged += (_, _) => { if (_theme.SelectedItem is string mode) SetTheme(mode); };
        actions.Children.Add(_theme);
        Put(header, actions, 1);
        Put(root, header, 0, 0);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        _span.SelectedIndex = Math.Max(0, Array.IndexOf(_spans, _settings.FollowSeconds));
        _follow.IsChecked = false;
        _follow.IsCheckedChanged += (_, _) =>
        {
            if (_changingUi)
                return;
            _settings.Follow = _follow.IsChecked == true;
            _settings.Save();
            if (_follow.IsChecked == true && _store.HasData)
                FollowLatest(true);
        };
        _span.SelectionChanged += (_, _) =>
        {
            if (_changingUi || _span.SelectedIndex < 0)
                return;
            _settings.FollowSeconds = _spans[_span.SelectedIndex];
            _settings.Save();
            if (_store.HasData)
            {
                double anchor = _follow.IsChecked == true ? _store.Snapshot.Bounds[1] : (_left + _right) / 2 + _settings.FollowSeconds / 2;
                Navigate(anchor - _settings.FollowSeconds, anchor, true, false);
            }
        };
        toolbar.Children.Add(_follow);
        toolbar.Children.Add(_span);
        toolbar.Children.Add(Button("全部历史", () => { if (_store.HasData) Navigate(_store.Snapshot.Bounds[0], _store.Snapshot.Bounds[1], true); }));
        toolbar.Children.Add(Button("刷新", () => _ = SyncCurrent(true)));
        toolbar.Children.Add(_from);
        toolbar.Children.Add(Text("—", 12, "#6d8299"));
        toolbar.Children.Add(_to);
        toolbar.Children.Add(Button("应用时间", ApplyDates));
        toolbar.Children.Add(Button("重新排列", () => Arrange()));
        var more = new Button { Content = "更多", Padding = new Thickness(12, 7) };
        var menu = CreateMenu();
        more.ContextMenu = menu;
        more.Click += (_, _) => menu.Open(more);
        toolbar.Children.Add(more);
        Put(root, toolbar, 0, 1);
        var toggles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 13 };
        foreach (string key in DataStore.Keys)
        {
            var check = new CheckBox { Content = key, IsChecked = _settings.VisibleChannels.Contains(key), FontSize = 11, MinHeight = 26 };
            check.Bind(ForegroundProperty, check.GetResourceObservable(UiTheme.Channels[key].Dark));
            _checks[key] = check;
            check.IsCheckedChanged += (_, _) =>
            {
                if (_changingUi)
                    return;
                _settings.VisibleChannels = DataStore.Keys.Where(k => _checks[k].IsChecked == true).ToArray();
                _settings.Save();
                SyncCards();
                SyncAxes();
                if (_data != null)
                {
                    Draw();
                    Arrange(true);
                }
                _cursorRendered = double.NaN;
            };
            toggles.Children.Add(check);
            var content = new StackPanel { Spacing = 4 };
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Put(line, Text(ChannelName(key), 11, UiTheme.Channels[key].Dark), 0);
            _modes[key] = Text("自动", 10, "#768ea6");
            Put(line, _modes[key], 1);
            content.Children.Add(line);
            var value = Text("—", 18, UiTheme.Channels[key].Dark);
            value.FontWeight = FontWeight.Medium;
            _values[key] = value;
            content.Children.Add(value);
            var card = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7), Margin = new Thickness(0, 0, 8, 0), Child = content };
            card.Bind(Border.BackgroundProperty, card.GetResourceObservable("Surface"));
            _cardByKey[key] = card;
            _cards.Children.Add(card);
        }
        toggles.Children.Add(_axisSelector);
        _axisSelector.SelectionChanged += (_, _) => { SyncAxes(); _plot.Refresh(); };
        var multi = new CheckBox { Content = "多纵轴", IsChecked = _settings.MultipleAxes };
        multi.IsCheckedChanged += (_, _) => { _settings.MultipleAxes = multi.IsChecked == true; _settings.Save(); SyncAxes(); _plot.Refresh(); };
        toggles.Children.Add(multi);
        _band.IsVisible = _settings.EventBandVisible;
        Put(root, toggles, 0, 2);
        SyncCards();
        Put(root, _cards, 0, 3);
        var chart = new Grid { MinHeight = 245, ClipToBounds = true };
        chart.Children.Add(_plot);
        chart.Children.Add(_overlay);
        var border = new Border { Child = chart, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("Border"));
        Put(root, border, 0, 4);
        _band.Events = _store.Snapshot.Events;
        _band.Picked += (item, jump) => { SelectEvent(item); if (jump) LastLoad = ShowEvent(item); };
        Put(root, _band, 0, 5);
        var readout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Put(readout, _cursorLabel, 0);
        var marks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        marks.Children.Add(Button("标记时间", () => { _marker = _cursorTime; _cursorRendered = double.NaN; UpdateOverlay(); }));
        marks.Children.Add(Button("清除标记", () => { _marker = null; _cursorRendered = double.NaN; UpdateOverlay(); }));
        Put(readout, marks, 1);
        Put(root, readout, 0, 6);
        var eventHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 18 };
        Put(eventHeader, _eventCount, 0);
        Put(eventHeader, Text("单击高亮 · 双击回看前后各 30 分钟", 11, "#849ab2"), 1);
        Put(eventHeader, _rangeLabel, 2);
        Put(root, eventHeader, 0, 7);
        var eventArea = new Grid { RowDefinitions = new RowDefinitions("26,*") };
        var columns = EventGrid();
        string[] labels = ["开始时间", "类型", "峰值 / mK", "升幅 / mK", "持续", "恢复时刻", "基线 / mK"];
        for (int i = 0; i < labels.Length; i++)
            Put(columns, Text(labels[i], 11, "#8096ac"), i);
        columns.Margin = new Thickness(14, 0, 14, 0);
        Put(eventArea, columns, 0, 0);
        _events.Bind(BackgroundProperty, _events.GetResourceObservable("Plot"));
        _events.BorderThickness = new Thickness(0);
        _events.ItemTemplate = new FuncDataTemplate<EventRecord>((item, _) =>
        {
            if (item == null)
                return new TextBlock();
            var row = EventGrid();
            row.Margin = new Thickness(5, 5);
            Put(row, Text(item.StartText), 0);
            Put(row, Text(item.Kind, 12, item.Fast ? "#f5b868" : "#83bcea"), 1);
            Put(row, Text(item.PeakText), 2);
            Put(row, Text(item.RiseText), 3);
            Put(row, Text(item.DurationText), 4);
            Put(row, Text(item.Recovered ? Clock.At(item.End).ToString("MM-dd HH:mm") : "区间内未恢复", 11, item.Recovered ? "#8fada7" : "#bca582"), 5);
            Put(row, Text((item.Baseline * 1000).ToString("F2")), 6);
            ToolTip.SetTip(row, item.Detail);
            return row;
        });
        _events.SelectionChanged += (_, _) => { if (_events.SelectedItem is EventRecord item) SelectEvent(item); };
        _events.DoubleTapped += (_, _) => { if (_selected != null) LastLoad = ShowEvent(_selected); };
        Put(eventArea, _events, 0, 1);
        Put(root, eventArea, 0, 8);
        var footer = new StackPanel { Spacing = 5 };
        _eventDetail.TextWrapping = TextWrapping.Wrap;
        footer.Children.Add(_eventDetail);
        footer.Children.Add(_status);
        footer.Children.Add(Text("图内拖动 / 滚轮：平移 / 缩放时间  ·  纵轴拖动 / 滚轮：仅调整该通道  ·  右键查看更多操作  ·  日志时间 UTC+08:00", 10, "#647d95"));
        Put(root, footer, 0, 9);
    }
    static Grid EventGrid() => new() { ColumnDefinitions = new ColumnDefinitions("1.4*,1.15*,*,*,*,1.4*,*"), ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
    static string ChannelName(string key) => key == "CH6" ? "CH6 · 混合室" : key == "CH5" ? "CH5 · Still" : key;
    static double Factor(string key) => key == "CH6" ? 1000 : 1;
    static string Unit(string key) => key == "CH6" ? "mK" : key.StartsWith("CH") ? "K" : "原值";
    void ConfigurePlot()
    {
        _plot.UserInputProcessor.Disable();
        var plot = _plot.Plot;
        plot.FigureBackground.Color = SColor.FromHex("#101c2a");
        plot.DataBackground.Color = SColor.FromHex("#101c2a");
        plot.Font.Set(UiTheme.Font);
        plot.Axes.Color(SColor.FromHex("#91a5ba"));
        plot.Axes.Left.IsVisible = false;
        plot.Axes.Right.IsVisible = false;
        plot.Axes.Top.IsVisible = false;
        SetDateAxis(plot);
        plot.Grid.MajorLineColor = SColor.FromHex("#243446");
        plot.Grid.MinorLineColor = SColor.FromHex("#182638");
        plot.Grid.YAxis = plot.Axes.Left;
        plot.Axes.SetLimitsY(0, 1);
        for (int i = 0; i < DataStore.Keys.Length; i++)
        {
            var key = DataStore.Keys[i];
            var axis = plot.Axes.AddRightAxis();
            axis.Color(SColor.FromHex(ChannelColor(key)));
            axis.LabelStyle.Text = key + " · " + Unit(key);
            axis.LabelStyle.FontSize = 10;
            axis.TickLabelStyle.FontSize = 10;
            axis.MinimumSize = key == "P2" ? 80 : 65;
            axis.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic { TargetTickCount = 4, LabelFormatter = v => v.ToString("G5", CultureInfo.InvariantCulture) };
            _axes[key] = axis;
        }
        _plot.AddHandler(PointerPressedEvent, PlotPressed, RoutingStrategies.Tunnel);
        _plot.AddHandler(PointerMovedEvent, PlotMoved, RoutingStrategies.Tunnel);
        _plot.AddHandler(PointerReleasedEvent, PlotReleased, RoutingStrategies.Tunnel);
        _plot.AddHandler(PointerWheelChangedEvent, PlotWheel, RoutingStrategies.Tunnel);
        _plot.ContextMenu = CreateMenu();
        _plot.PointerCaptureLost += (_, _) => { _dragging = false; _dragChannel = null; };
    }
    ScottPlot.IXAxis SetDateAxis(ScottPlot.Plot plot)
    {
        var axis = plot.Axes.DateTimeTicksBottom();
        axis.Color(SColor.FromHex("#91a5ba"));
        axis.TickGenerator = new ScottPlot.TickGenerators.DateTimeAutomatic { MaxTickCount = 8, LabelFormatter = d => (_right - _left) > 3 * 86400 ? d.ToString("MM-dd") : d.ToString("MM-dd\nHH:mm") };
        axis.TickLabelStyle.FontSize = 11;
        return axis;
    }
    void ApplyDates()
    {
        if (_from.Value is not DateTime start || _to.Value is not DateTime end || end <= start)
        {
            _viewModel.Status = "请选择有效的起止日期和时间，结束时间须晚于开始时间。";
            return;
        }
        Navigate(Clock.Unix(start), Clock.Unix(end), true);
    }
    void Navigate(double left, double right, bool arrange = false, bool stopFollow = true)
    {
        if (stopFollow)
            StopFollowing();
        SetRange(left, right);
        _arrangePending |= arrange;
        _from.Set(_left);
        _to.Set(_right);
        LastLoad = LoadVisible();
    }
    void SetRange(double left, double right)
    {
        if (!double.IsFinite(left) || !double.IsFinite(right) || right <= left)
            return;
        double span = Math.Clamp(right - left, 60, Math.Max(365 * 86400, (_store.Snapshot.Bounds[1] - _store.Snapshot.Bounds[0]) * 1.2));
        double center = (left + right) / 2;
        _left = center - span / 2;
        _right = center + span / 2;
        _plot.Plot.Axes.SetLimitsX(Clock.Plot(_left), Clock.Plot(_right));
        _rangeLabel.Text = $"{Clock.At(_left):MM-dd HH:mm} — {Clock.At(_right):MM-dd HH:mm}";
        _plot.Refresh();
        UpdateOverlay();
    }
    async Task LoadVisible(bool force = false)
    {
        if (!_store.HasData)
            return;
        var store = _store;
        var revision = store.Revision;
        if (!force && _dataRevision == revision && _data != null && _data.Left <= _left && _data.Right >= _right && _data.Resolution == DataStore.ResolutionFor(_right - _left))
        {
            if (_arrangePending)
            {
                Arrange();
                _arrangePending = false;
            }
            return;
        }
        _loadCancel?.Cancel();
        var cancellation = new CancellationTokenSource();
        _loadCancel = cancellation;
        double span = _right - _left, left = _left - span * .2, right = _right + span * .2;
        _viewModel.Status = "正在读取当前时间范围…";
        try
        {
            var data = await Task.Run(() => store.Read(left, right, span, cancellation.Token));
            if (cancellation.IsCancellationRequested || _closed || !ReferenceEquals(store, _store) || revision != store.Revision)
                return;
            _data = data;
            _dataRevision = revision;
            var drawTimer = Stopwatch.StartNew();
            Draw();
            if (_arrangePending)
            {
                Arrange();
                _arrangePending = false;
            }
            _viewModel.Status = $"{data.Count:N0} 个绘图点 · {(data.Resolution == 0 ? "原始样本" : $"{data.Resolution / 60} 分钟极值概览")} · 读取 {data.Milliseconds:F0} ms / 准备绘图 {drawTimer.Elapsed.TotalMilliseconds:F0} ms · 光标查原始记录 · 共 {_store.Snapshot.Counts.Values.Sum():N0} 条";
            _cursorRendered = double.NaN;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _viewModel.Status = "读取失败：" + ex.Message; Console.Error.WriteLine(ex); }
    }
    void Draw()
    {
        if (_data is null)
            return;
        SyncAxes();
        _shade = PlotController.Render(_plot.Plot, _data, _axes,
            _checks.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToHashSet(), ChannelColor);
        UpdateSelection();
        _plot.Refresh();
    }
    void Arrange(bool keepManual = false)
    {
        if (_data is null)
            return;
        var keys = DataStore.Keys.Where(k => _checks[k].IsChecked == true).ToArray();
        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            if (keepManual && _manual.Contains(key))
                continue;
            var ys = _data.Series[key].Points.Where(p => p.T >= _left && p.T <= _right).Select(p => p.V * Factor(key)).ToArray();
            double min = ys.Length == 0 ? 0 : ys.Min(), max = ys.Length == 0 ? 1 : ys.Max();
            double height = Math.Max(max - min, Math.Max(Math.Abs((max + min) / 2) * .0002, .000001));
            double total = height * keys.Length * 1.38, center = (max + min) / 2;
            double position = 1 - (i + .5) / keys.Length;
            _plot.Plot.Axes.SetLimitsY(center - position * total, center + (1 - position) * total, _axes[key]);
        }
        if (!keepManual)
            _manual.Clear();
        UpdateModes();
        _plot.Refresh();
    }
    void UpdateModes()
    {
        foreach (var key in DataStore.Keys)
            _modes[key].Text = _manual.Contains(key) ? "手动" : "自动";
    }
    void SelectEvent(EventRecord item)
    {
        _selected = item;
        if (!ReferenceEquals(_events.SelectedItem, item))
        {
            _events.SelectedItem = item;
            _events.ScrollIntoView(item);
        }
        _eventDetail.Text = item.Detail;
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
        if (_from.Value is null)
        {
            _from.Set(_left);
            _to.Set(_right);
        }
        LastLoad = LoadVisible();
        await LastLoad;
    }
    Rect DataRect()
    {
        var rect = _plot.Plot.LastRender.DataRect;
        double scale = _plot.DisplayScale;
        return new Rect(rect.Left / scale, rect.Top / scale, Math.Max(0, rect.Width / scale), Math.Max(0, rect.Height / scale));
    }
    string? AxisAt(Point point)
    {
        var axis = _plot.Plot.GetAxis(new ScottPlot.Pixel((float)(point.X * _plot.DisplayScale), (float)(point.Y * _plot.DisplayScale)));
        return _axes.FirstOrDefault(p => ReferenceEquals(p.Value, axis)).Key;
    }
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
        _dragLeft = _left;
        _dragRight = _right;
        if (key != null)
        {
            _axisSelector.SelectedItem = key;
            _dragMin = _axes[key].Min;
            _dragMax = _axes[key].Max;
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
                double shift = (point.Y - _dragPoint.Y) / rect.Height * (_dragMax - _dragMin);
                _plot.Plot.Axes.SetLimitsY(_dragMin + shift, _dragMax + shift, _axes[key]);
                _manual.Add(key);
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
            _cursorTime = _left + (point.X - rect.Left) / rect.Width * (_right - _left);
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
            var axis = _axes[key];
            double anchor = axis.Max - (point.Y - rect.Top) / rect.Height * (axis.Max - axis.Min);
            ZoomChannel(key, factor, anchor);
        }
        else if (rect.Contains(point))
        {
            StopFollowing();
            double anchor = _left + (point.X - rect.Left) / rect.Width * (_right - _left);
            SetRange(anchor + (_left - anchor) * factor, anchor + (_right - anchor) * factor);
            QueueLoad();
        }
        e.Handled = true;
    }
    void ZoomChannel(string key, double factor, double anchor)
    {
        var axis = _axes[key];
        _plot.Plot.Axes.SetLimitsY(anchor + (axis.Min - anchor) * factor, anchor + (axis.Max - anchor) * factor, axis);
        _manual.Add(key);
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
        if (rect.Width <= 0 || _right <= _left)
            return;
        double X(double t) => rect.Left + (t - _left) / (_right - _left) * rect.Width;
        double? cursor = _cursorTime.HasValue ? X(_cursorTime.Value) : null;
        double? marker = _marker.HasValue ? X(_marker.Value) : null;
        if (_overlay.DataRect != rect || _overlay.CursorX != cursor || _overlay.MarkerX != marker)
        {
            _overlay.DataRect = rect;
            _overlay.CursorX = cursor;
            _overlay.MarkerX = marker;
            _overlay.InvalidateVisual();
        }
        if (_band.Left != _left || _band.Right != _right || _band.PlotLeft != rect.Left + 1 || _band.PlotRight != rect.Right + 1)
        {
            _band.Left = _left;
            _band.Right = _right;
            _band.PlotLeft = rect.Left + 1;
            _band.PlotRight = rect.Right + 1;
            _band.InvalidateVisual();
        }
    }
    async Task UpdateReadout()
    {
        if (!_store.HasData || _cursorTime is not double time || _cursorBusy || _cursorRendered == time)
            return;
        _cursorBusy = true;
        var store = _store;
        try
        {
            long bucket = (long)Math.Floor(time / 300);
            if (bucket != _cursorBucket)
            {
                _cursorSamples = await Task.Run(() => store.ReadCursorBucket(bucket));
                _cursorBucket = bucket;
            }
            if (_closed || !ReferenceEquals(store, _store))
            {
                _cursorBucket = long.MinValue;
                return;
            }
            foreach (var key in DataStore.Keys)
            {
                var sample = DataStore.Nearest(_cursorSamples.GetValueOrDefault(key, []), time);
                _values[key].Text = _checks[key].IsChecked != true ? "已隐藏" : sample == null ? "无记录" : (sample.V * Factor(key)).ToString(key == "CH6" ? "F3" : "G6", CultureInfo.InvariantCulture) + " " + Unit(key);
                ToolTip.SetTip(_values[key], sample == null ? "光标前后 90 秒内没有原始记录" : $"原始样本 {Clock.At(sample.T):yyyy-MM-dd HH:mm:ss} · 与光标相差 {sample.T - time:+0.0;-0.0;0} 秒");
            }
            _cursorLabel.Text = $"{Clock.At(time):yyyy-MM-dd HH:mm:ss}   ·   原始读数" + (_marker.HasValue ? $"   ·   距标记 {(time - _marker.Value) / 60:+0.00;-0.00;0} min" : "");
            _cursorRendered = time;
        }
        catch (Exception ex) { _cursorLabel.Text = "读数读取失败：" + ex.Message; }
        finally { _cursorBusy = false; }
    }
}
