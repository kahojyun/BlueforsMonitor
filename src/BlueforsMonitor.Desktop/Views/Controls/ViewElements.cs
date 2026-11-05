using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace BlueforsAvalonia;

internal static class ViewElements
{
    public static TextBlock Text(string text, double size = 12, string color = "Text")
    {
        var label = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        UiTheme.Foreground(label, color);
        return label;
    }
    public static Button Button(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(12, 7) };
        button.Click += (_, _) => action();
        return button;
    }
    public static void Put(Grid grid, Control child, int column, int row = 0)
    {
        Grid.SetColumn(child, column);
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }
}
