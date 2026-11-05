using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Styling;
using Avalonia.Data;
using static BlueforsAvalonia.ViewElements;

namespace BlueforsAvalonia;

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
        Put(this, _detail, 0, 2);
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
            _detail.Text = item?.Detail ?? "点击事件查看同期 Still、流量和压力变化。";
        }
        finally { _updating = false; }
    }
    public void SetRange(double left, double right) =>
        _rangeLabel.Text = $"{Clock.At(left):MM-dd HH:mm} — {Clock.At(right):MM-dd HH:mm}";
}
