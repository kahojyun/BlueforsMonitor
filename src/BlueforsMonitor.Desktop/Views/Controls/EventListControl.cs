using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Styling;
using Avalonia.Data;
using static BlueforsMonitor.ViewElements;

namespace BlueforsMonitor;

/// <summary>Event presentation and user intent, independent of the plot and data source.</summary>
public sealed class EventListControl : Grid
{
    public static readonly StyledProperty<string> CountTextProperty = AvaloniaProperty.Register<EventListControl, string>(nameof(CountText), "升温事件 0");
    public string CountText
    {
        get => GetValue(CountTextProperty); set => SetValue(CountTextProperty, value);
    }
    readonly TableView _events = new() { Name = "EventRows", CanUserResizeColumns = true };
    readonly TextBlock _eventCount = Text("", 14);
    readonly TextBlock _expandIcon = Text("▾", 12, "SecondaryText");
    readonly TextBlock _detail = Text("点击事件查看同期 Still、流量和压力变化。", 11, "SecondaryText");
    readonly Button _expand = new() { Name = "EventsToggle", Padding = new Thickness(6, 3) };
    readonly CheckBox _detailsToggle = new() { Name = "DetailsToggle", Content = "详情", MinHeight = 26 };
    readonly ScrollViewer _detailArea = new() { MaxHeight = 72 };
    bool _expanded = true, _detailsExpanded = true;
    public bool Expanded => _expanded;
    public bool DetailsExpanded => _detailsExpanded;
    public event Action<bool>? ExpandedChanged;
    public event Action<bool>? DetailsExpandedChanged;
    bool _updating;
    public EventRecord? SelectedEvent => _events.SelectedItem as EventRecord;
    public event Action<EventRecord>? SelectionRequested;
    public event Action<EventRecord>? ReplayRequested;

    public EventListControl()
    {
        RowDefinitions = new RowDefinitions("Auto,*,Auto");
        RowSpacing = 4;
        _eventCount.Bind(TextBlock.TextProperty, this.GetObservable(CountTextProperty));
        var eventHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 18 };
        var caption = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        caption.Children.Add(_expandIcon);
        caption.Children.Add(_eventCount);
        _expand.Content = caption;
        _expand.Click += (_, _) => { SetExpanded(!_expanded); ExpandedChanged?.Invoke(_expanded); };
        ToolTip.SetTip(_expand, "展开 / 收起事件表");
        Put(eventHeader, _expand, 0);
        Put(eventHeader, Text("单击高亮 · 双击回看前后各 30 分钟", 11, "SecondaryText"), 1);
        _detailsToggle.IsChecked = true;
        _detailsToggle.IsCheckedChanged += (_, _) =>
        {
            if (_updating)
                return;
            _detailsExpanded = _detailsToggle.IsChecked == true;
            UpdatePanels();
            DetailsExpandedChanged?.Invoke(_detailsExpanded);
        };
        Put(eventHeader, _detailsToggle, 2);
        Put(this, eventHeader, 0, 0);
        _events.Bind(TemplatedControl.BackgroundProperty, _events.GetResourceObservable("Plot"));
        _events.BorderThickness = new Thickness(0);
        _events.Styles.Add(new Style(x => x.OfType<TableViewRow>())
        {
            Setters = { new Setter(ToolTip.TipProperty, CompiledBinding.Create((EventRecord item) => item.Detail)) }
        });
        AddColumn("开始时间", 1.4, item => item.StartText);
        AddColumn("类型", 1.15, item => item.Kind, item => item.Fast ? "EventFast" : "EventSlow");
        AddColumn("峰值 / mK", 1, item => item.PeakText);
        AddColumn("升幅 / mK", 1, item => item.RiseText);
        AddColumn("持续", 1, item => item.DurationText);
        AddColumn("恢复时刻", 1.4, item => item.Recovered ? Clock.At(item.End).ToString("MM-dd HH:mm") : "区间内未恢复",
            item => item.Recovered ? "Recovered" : "Pending");
        AddColumn("基线 / mK", 1, item => (item.Baseline * 1000).ToString("F2"));
        _events.SelectionChanged += (_, _) =>
        {
            if (!_updating && _events.SelectedItem is EventRecord item)
            {
                _detail.Text = item.Detail;
                UpdatePanels();
                SelectionRequested?.Invoke(item);
            }
        };
        _events.DoubleTapped += (_, e) =>
        {
            // Headers and resize handles are inside TableView too; only rows request replay.
            if (e.Source is Visual source &&
                (source is TableViewRow || source.GetVisualAncestors().OfType<TableViewRow>().Any()))
                ReplaySelected();
        };
        Put(this, _events, 0, 1);
        _detail.TextWrapping = TextWrapping.Wrap;
        _detailArea.Content = _detail;
        Put(this, _detailArea, 0, 2);
        SizeChanged += (_, _) => UpdatePanels();
        UpdatePanels();
    }
    public void SetExpanded(bool value)
    {
        _expanded = value;
        UpdatePanels();
    }
    public void SetDetailsExpanded(bool value)
    {
        _detailsExpanded = value;
        _updating = true;
        try
        {
            _detailsToggle.IsChecked = value;
        }
        finally { _updating = false; }
        UpdatePanels();
    }
    void UpdatePanels()
    {
        _expandIcon.Text = _expanded ? "▾" : "▸";
        _detailArea.MaxHeight = Math.Clamp(Bounds.Height - _expand.Bounds.Height - 98, 0, 72);
        _events.IsVisible = _expanded;
        RowDefinitions[1].Height = _expanded ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        _detailsToggle.IsVisible = _expanded && SelectedEvent != null;
        _detailArea.IsVisible = _expanded && _detailsExpanded && SelectedEvent != null;
    }
    void AddColumn(string header, double width, Func<EventRecord, string> text, Func<EventRecord, string>? color = null)
    {
        _events.Columns.Add(new TableViewColumn
        {
            Header = header,
            Width = new GridLength(width, GridUnitType.Star),
            HeaderTemplate = new FuncDataTemplate<string>((value, _) => Text(value ?? "", 11, "SecondaryText")),
            CellTemplate = new FuncDataTemplate<EventRecord>((item, _) =>
            {
                if (item == null)
                    return new TextBlock();
                var label = Text(text(item), 12, color?.Invoke(item) ?? "Text");
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                ToolTip.SetTip(label, item.Detail);
                return label;
            })
        });
    }
    public void ReplaySelected()
    {
        if (SelectedEvent is EventRecord item)
            ReplayRequested?.Invoke(item);
    }
    public void SetEvents(IEnumerable<EventRecord> events)
    {
        _updating = true;
        try
        {
            _events.ItemsSource = events;
        }
        finally { _updating = false; }
    }
    public void Select(EventRecord? item)
    {
        _updating = true;
        try
        {
            if (!ReferenceEquals(_events.SelectedItem, item))
            {
                _events.SelectedItem = item;
                if (item != null)
                    _events.ScrollIntoView(item);
            }
            _detail.Text = item?.Detail ?? "";
            UpdatePanels();
        }
        finally { _updating = false; }
    }
    public void SetRange(double left, double right) =>
        ToolTip.SetTip(_expand, $"展开 / 收起事件表 · {Clock.At(left):MM-dd HH:mm} — {Clock.At(right):MM-dd HH:mm}");
}
