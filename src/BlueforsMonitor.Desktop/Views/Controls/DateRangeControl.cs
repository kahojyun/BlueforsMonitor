using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using static BlueforsMonitor.ViewElements;

namespace BlueforsMonitor;

/// <summary>Owns date entry and validation; navigation is requested only for a valid range.</summary>
public sealed class DateRangeControl : StackPanel
{
    public DateTimeField Start { get; } = new();
    public DateTimeField End { get; } = new();
    readonly TextBlock _error = Text("", 12, "Error");
    public string? ValidationMessage => _error.IsVisible ? _error.Text : null;
    public event Action<DateTime, DateTime>? RangeApplied;

    public DateRangeControl()
    {
        Spacing = 7;
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, field) in new[] { ("开始", Start), ("结束", End) })
        {
            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 16, 4) };
            group.Children.Add(Text(label));
            group.Children.Add(field);
            row.Children.Add(group);
        }
        row.Children.Add(Button("应用时间", Apply));
        _error.IsVisible = false;
        _error.TextWrapping = TextWrapping.Wrap;
        Children.Add(row);
        Children.Add(_error);
    }
    public void Apply()
    {
        DateTime end = default;
        string error;
        if (!Start.TryGetValue(out var start, out error))
            error = "开始时间：" + error;
        else if (!End.TryGetValue(out end, out error))
            error = "结束时间：" + error;
        else if (end <= start)
            error = "结束时间须晚于开始时间。";
        _error.Text = error;
        _error.IsVisible = error.Length > 0;
        if (error.Length == 0)
            RangeApplied?.Invoke(start, end);
    }
    // Programmatic navigation updates the editors without requesting another navigation.
    public void SetRange(double start, double end)
    {
        _error.Text = "";
        _error.IsVisible = false;
        Start.Set(start);
        End.Set(end);
    }
}
