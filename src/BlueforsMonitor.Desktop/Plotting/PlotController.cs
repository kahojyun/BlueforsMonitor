using System.Globalization;
namespace BlueforsAvalonia;

public static class PlotController
{
    public static ScottPlot.Plottables.HorizontalSpan Render(ScottPlot.Plot plot, WindowData data,
        IReadOnlyDictionary<string, ScottPlot.AxisPanels.RightAxis> axes, IReadOnlySet<string> visibleKeys,
        Func<string, string> color, bool dark)
    {
        plot.Clear();
        var shade = plot.Add.HorizontalSpan(0, 0);
        shade.FillColor = ScottPlot.Color.FromHex(UiTheme.Hex("EventSlow", dark)).WithAlpha(.10);
        shade.LineWidth = 0;
        shade.EnableAutoscale = false;
        foreach (var key in DataStore.Keys)
        {
            bool visible = visibleKeys.Contains(key);
            if (!visible)
                continue;
            var values = data.Series[key];
            var xs = new List<double>();
            var ys = new List<double>();
            axes[key].TickGenerator = values.Points.Length == 0
                ? new ScottPlot.TickGenerators.NumericManual()
                : new ScottPlot.TickGenerators.NumericAutomatic { TargetTickCount = 4, LabelFormatter = v => v.ToString("G5", CultureInfo.InvariantCulture) };
            foreach (var p in values.Points)
            {
                if (values.Gaps.Contains(p.T) && xs.Count > 0)
                {
                    xs.Add(Clock.Plot(p.T));
                    ys.Add(double.NaN);
                }
                xs.Add(Clock.Plot(p.T));
                ys.Add(p.V * (key == "CH6" ? 1000 : 1));
            }
            if (xs.Count == 0)
                continue;
            var scatter = plot.Add.Scatter(xs.ToArray(), ys.ToArray(), ScottPlot.Color.FromHex(color(key)));
            scatter.Axes.YAxis = axes[key];
            scatter.MarkerSize = 0;
            scatter.LineWidth = 1.5f;
        }
        return shade;
    }
}
