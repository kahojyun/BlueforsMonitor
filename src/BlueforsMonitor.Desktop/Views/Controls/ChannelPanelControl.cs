using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using static BlueforsAvalonia.ViewElements;

namespace BlueforsAvalonia;

/// <summary>Owns channel visibility, readout cards and axis-selection UI; never reads or saves settings.</summary>
public sealed class ChannelPanelControl : StackPanel
{
    readonly Dictionary<string, CheckBox> _checks = new();
    readonly Dictionary<string, TextBlock> _values = new();
    readonly Dictionary<string, TextBlock> _modes = new();
    readonly Dictionary<string, Border> _cardByKey = new();
    readonly UniformGrid _cards = new() { Rows = 1 };
    readonly ComboBox _axisSelector = new() { Name = "AxisSelector", Width = 90, ItemsSource = DataStore.Keys, SelectedIndex = 0 };
    readonly CheckBox _multiple = new() { Name = "MultipleAxes", Content = "多纵轴" };
    bool _updating;
    public string[] VisibleChannels => DataStore.Keys.Where(IsChannelVisible).ToArray();
    public string? SelectedAxis { get => _axisSelector.SelectedItem as string; set => _axisSelector.SelectedItem = value; }
    public bool MultipleAxes { get => _multiple.IsChecked == true; set => _multiple.IsChecked = value; }
    public event Action? VisibilityChanged;
    public event Action? AxisOptionsChanged;

    public ChannelPanelControl()
    {
        Spacing = 9;
        var toggles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 13 };
        foreach (string key in DataStore.Keys)
        {
            var check = new CheckBox { Name = "Channel" + key, Content = key, FontSize = 11, MinHeight = 26 };
            check.Bind(TemplatedControl.ForegroundProperty, check.GetResourceObservable(UiTheme.ChannelResource(key)));
            _checks[key] = check;
            check.IsCheckedChanged += (_, _) =>
            {
                if (_updating) return;
                SyncCards();
                VisibilityChanged?.Invoke();
            };
            toggles.Children.Add(check);
            var content = new StackPanel { Spacing = 4 };
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Put(line, Text(ChannelName(key), 11, UiTheme.ChannelResource(key)), 0);
            _modes[key] = Text("自动", 10, "SecondaryText");
            Put(line, _modes[key], 1);
            content.Children.Add(line);
            var value = Text("—", 18, UiTheme.ChannelResource(key));
            value.FontWeight = FontWeight.Medium;
            _values[key] = value;
            content.Children.Add(value);
            var card = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 7), Margin = new Thickness(0, 0, 8, 0), Child = content };
            card.Bind(Border.BackgroundProperty, card.GetResourceObservable("Surface"));
            _cardByKey[key] = card;
            _cards.Children.Add(card);
        }
        toggles.Children.Add(_axisSelector);
        toggles.Children.Add(_multiple);
        _axisSelector.SelectionChanged += (_, _) => { if (!_updating) AxisOptionsChanged?.Invoke(); };
        _multiple.IsCheckedChanged += (_, _) => { if (!_updating) AxisOptionsChanged?.Invoke(); };
        Children.Add(toggles);
        Children.Add(_cards);
        SyncCards();
    }
    public void Configure(IEnumerable<string> visible, bool multipleAxes)
    {
        var keys = visible.ToHashSet();
        _updating = true;
        try
        {
            foreach (var pair in _checks)
                pair.Value.IsChecked = keys.Contains(pair.Key);
            MultipleAxes = multipleAxes;
        }
        finally { _updating = false; }
        SyncCards();
    }
    public bool IsChannelVisible(string key) => _checks[key].IsChecked == true;
    public void SetChannelVisible(string key, bool visible) => _checks[key].IsChecked = visible;
    public void SetReadout(string key, string text, string tooltip)
    {
        _values[key].Text = text;
        ToolTip.SetTip(_values[key], tooltip);
    }
    public void SetManualChannels(IReadOnlySet<string> manual)
    {
        foreach (var key in DataStore.Keys)
            _modes[key].Text = manual.Contains(key) ? "手动" : "自动";
    }
    void SyncCards()
    {
        _cards.Columns = Math.Max(1, VisibleChannels.Length);
        foreach (var pair in _cardByKey)
            pair.Value.IsVisible = IsChannelVisible(pair.Key);
    }
    static string ChannelName(string key) => key == "CH6" ? "CH6 · 混合室" : key == "CH5" ? "CH5 · Still" : key;
}
