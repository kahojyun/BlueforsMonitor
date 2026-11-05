using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using static BlueforsAvalonia.ViewElements;

namespace BlueforsAvalonia;

/// <summary>Owns channel visibility, compact readouts and axis-selection UI; never reads or saves settings.</summary>
public sealed class ChannelPanelControl : StackPanel
{
    readonly Dictionary<string, CheckBox> _checks = new();
    readonly Dictionary<string, TextBlock> _values = new();
    readonly Dictionary<string, TextBlock> _modes = new();
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
        Spacing = 3;
        var toggles = new WrapPanel { Orientation = Orientation.Horizontal };
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
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            ToolTip.SetTip(check, ChannelName(key));
            content.Children.Add(check);
            var value = Text("—", 12, UiTheme.ChannelResource(key));
            value.Name = "Readout" + key;
            value.FontWeight = FontWeight.Medium;
            value.MaxWidth = 150;
            value.TextTrimming = TextTrimming.CharacterEllipsis;
            _values[key] = value;
            content.Children.Add(value);
            var mode = Text("手动", 10, "SecondaryText");
            mode.IsVisible = false;
            _modes[key] = mode;
            content.Children.Add(mode);
            var chip = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 0), Margin = new Thickness(0, 0, 6, 3), Child = content };
            chip.Bind(Border.BackgroundProperty, chip.GetResourceObservable("Surface"));
            toggles.Children.Add(chip);
        }
        _axisSelector.Margin = new Thickness(0, 0, 8, 3);
        _multiple.Margin = new Thickness(0, 0, 0, 3);
        toggles.Children.Add(_axisSelector);
        toggles.Children.Add(_multiple);
        _axisSelector.SelectionChanged += (_, _) => { if (!_updating) AxisOptionsChanged?.Invoke(); };
        _multiple.IsCheckedChanged += (_, _) => { if (!_updating) AxisOptionsChanged?.Invoke(); };
        Children.Add(toggles);
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
            _modes[key].IsVisible = manual.Contains(key) && IsChannelVisible(key);
    }
    void SyncCards()
    {
        foreach (var pair in _values)
        {
            pair.Value.IsVisible = IsChannelVisible(pair.Key);
            if (!IsChannelVisible(pair.Key)) _modes[pair.Key].IsVisible = false;
        }
    }
    static string ChannelName(string key) => key == "CH6" ? "CH6 · 混合室" : key == "CH5" ? "CH5 · Still" : key;
}
