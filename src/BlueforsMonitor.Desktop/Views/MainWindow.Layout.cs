using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using static BlueforsAvalonia.ViewElements;

namespace BlueforsAvalonia;

public sealed partial class MainWindow
{
    readonly ToggleButton _dateToggle = new() { Name = "DateToggle", Content = "自定义时间" };
    readonly ToggleButton _focusToggle = new() { Name = "FocusToggle", Content = "专注曲线" };
    readonly TextBlock _timeSummary = Text("尚无时间范围", 11, "SecondaryText");
    readonly Grid _workspace = new();
    readonly GridSplitter _splitter = new()
    {
        Name = "EventSplitter",
        Height = 6,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        ResizeDirection = GridResizeDirection.Rows,
        ResizeBehavior = GridResizeBehavior.PreviousAndNext
    };
    bool _applyingLayout;
    void InitializeLayout()
    {
        _splitter.Bind(BackgroundProperty, _splitter.GetResourceObservable("Border"));
        ToolTip.SetTip(_splitter, "拖动调整事件表高度；聚焦后也可用上下方向键调整");
        _dateToggle.IsChecked = _settings.Layout.DatesExpanded;
        _focusToggle.IsChecked = _settings.Layout.FocusMode;
        _eventList.SetExpanded(_settings.Layout.EventsExpanded);
        _eventList.SetDetailsExpanded(_settings.Layout.DetailsExpanded);
        _dateToggle.IsCheckedChanged += (_, _) =>
        {
            _settings.Layout.DatesExpanded = _dateToggle.IsChecked == true;
            SaveLayout();
        };
        _focusToggle.IsCheckedChanged += (_, _) =>
        {
            _settings.Layout.FocusMode = _focusToggle.IsChecked == true;
            SaveLayout();
        };
        _eventList.ExpandedChanged += value => { _settings.Layout.EventsExpanded = value; SaveLayout(); };
        _eventList.DetailsExpandedChanged += value => { _settings.Layout.DetailsExpanded = value; _settings.Save(); };
        _splitter.DragCompleted += (_, _) => Dispatcher.UIThread.Post(SavePanelHeight);
        _splitter.KeyUp += (_, _) => Dispatcher.UIThread.Post(SavePanelHeight);
        _workspace.SizeChanged += (_, _) => ApplyLayout();
        ApplyLayout();
    }
    void SaveLayout()
    {
        _settings.Save();
        ApplyLayout();
    }
    void SavePanelHeight()
    {
        if (_settings.Layout.FocusMode || !_settings.Layout.EventsExpanded)
            return;
        _settings.Layout.EventPanelHeight = _eventList.Bounds.Height;
        _settings.Layout.Normalize();
        SaveLayout();
    }
    void ApplyLayout()
    {
        if (_applyingLayout)
            return;
        _applyingLayout = true;
        try
        {
            var layout = _settings.Layout;
            _dateRange.IsVisible = layout.DatesExpanded && !layout.FocusMode;
            _dateToggle.IsEnabled = !layout.FocusMode;
            _focusToggle.Content = layout.FocusMode ? "退出专注" : "专注曲线";
            _eventList.IsVisible = !layout.FocusMode;
            _eventList.SetExpanded(layout.EventsExpanded);
            _splitter.IsVisible = !layout.FocusMode && layout.EventsExpanded;
            _workspace.RowDefinitions[1].Height = new GridLength(_splitter.IsVisible ? 6 : 0);
            var eventRow = _workspace.RowDefinitions[2];
            eventRow.MinHeight = _splitter.IsVisible ? 120 : 0;
            eventRow.MaxHeight = _workspace.Bounds.Height > 0 ? Math.Max(120, _workspace.Bounds.Height - 240) : 500;
            eventRow.Height = layout.FocusMode ? new GridLength(0) : !layout.EventsExpanded ? GridLength.Auto :
                new GridLength(Math.Clamp(layout.EventPanelHeight, 120, eventRow.MaxHeight));
            _workspace.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        }
        finally { _applyingLayout = false; }
    }
}
