using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using static BlueforsAvalonia.ViewElements;

namespace BlueforsAvalonia;

/// <summary>Event presentation and user intent, independent of the plot and data source.</summary>
public sealed class EventListControl : Grid
{
    public static readonly StyledProperty<string> CountTextProperty = AvaloniaProperty.Register<EventListControl, string>(nameof(CountText), "升温事件 0");
    public string CountText { get => GetValue(CountTextProperty); set => SetValue(CountTextProperty, value); }
    readonly ListBox _events = new() { Name = "EventRows" };
    readonly TextBlock _eventCount = Text("", 14);
    readonly TextBlock _rangeLabel = Text("", 11, "SecondaryText");
    readonly TextBlock _detail = Text("点击事件查看同期 Still、流量和压力变化。", 11, "SecondaryText");
    bool _updating;
    public EventRecord? SelectedEvent => _events.SelectedItem as EventRecord;
    public event Action<EventRecord>? SelectionRequested;
    public event Action<EventRecord>? ReplayRequested;

    public EventListControl()
    {
        RowDefinitions = new RowDefinitions("Auto,170,Auto");
        RowSpacing = 9;
        _eventCount.Bind(TextBlock.TextProperty, this.GetObservable(CountTextProperty));
        var eventHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 18 };
        Put(eventHeader, _eventCount, 0);
        Put(eventHeader, Text("单击高亮 · 双击回看前后各 30 分钟", 11, "SecondaryText"), 1);
        Put(eventHeader, _rangeLabel, 2);
        Put(this, eventHeader, 0, 0);
        var eventArea = new Grid { RowDefinitions = new RowDefinitions("26,*") };
        var columns = EventGrid();
        string[] labels = ["开始时间", "类型", "峰值 / mK", "升幅 / mK", "持续", "恢复时刻", "基线 / mK"];
        for (int i = 0; i < labels.Length; i++)
            Put(columns, Text(labels[i], 11, "SecondaryText"), i);
        columns.Margin = new Thickness(14, 0, 14, 0);
        Put(eventArea, columns, 0, 0);
        _events.Bind(TemplatedControl.BackgroundProperty, _events.GetResourceObservable("Plot"));
        _events.BorderThickness = new Thickness(0);
        _events.ItemTemplate = new FuncDataTemplate<EventRecord>((item, _) =>
        {
            if (item == null)
                return new TextBlock();
            var row = EventGrid();
            row.Margin = new Thickness(5, 5);
            Put(row, Text(item.StartText), 0);
            Put(row, Text(item.Kind, 12, item.Fast ? "EventFast" : "EventSlow"), 1);
            Put(row, Text(item.PeakText), 2);
            Put(row, Text(item.RiseText), 3);
            Put(row, Text(item.DurationText), 4);
            Put(row, Text(item.Recovered ? Clock.At(item.End).ToString("MM-dd HH:mm") : "区间内未恢复", 11, item.Recovered ? "Recovered" : "Pending"), 5);
            Put(row, Text((item.Baseline * 1000).ToString("F2")), 6);
            ToolTip.SetTip(row, item.Detail);
            return row;
        });
        _events.SelectionChanged += (_, _) =>
        {
            if (!_updating && _events.SelectedItem is EventRecord item)
            {
                _detail.Text = item.Detail;
                SelectionRequested?.Invoke(item);
            }
        };
        _events.DoubleTapped += (_, _) => ReplaySelected();
        Put(eventArea, _events, 0, 1);
        Put(this, eventArea, 0, 1);
        _detail.TextWrapping = TextWrapping.Wrap;
        Put(this, _detail, 0, 2);
    }
    static Grid EventGrid() => new() { ColumnDefinitions = new ColumnDefinitions("1.4*,1.15*,*,*,*,1.4*,*"), ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
    public void ReplaySelected()
    {
        if (SelectedEvent is EventRecord item)
            ReplayRequested?.Invoke(item);
    }
    public void SetEvents(IEnumerable<EventRecord> events)
    {
        _updating = true;
        try { _events.ItemsSource = events; }
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
            _detail.Text = item?.Detail ?? "点击事件查看同期 Still、流量和压力变化。";
        }
        finally { _updating = false; }
    }
    public void SetRange(double left, double right) =>
        _rangeLabel.Text = $"{Clock.At(left):MM-dd HH:mm} — {Clock.At(right):MM-dd HH:mm}";
}
