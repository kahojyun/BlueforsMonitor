using BlueforsAvalonia;

static class AxisControllerChecks
{
    public static void Run()
    {
        using var plot = new ScottPlot.Plot();
        var controller = new AxisController(plot, true);
        void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
        controller.SetRange(0, 1000, 1000);
        var data = new WindowData(DataStore.Keys.ToDictionary(k => k, k => new SeriesData([new(100, k == "CH6" ? .012 : 2), new(200, k == "CH6" ? .028 : 4)], [])), 0, 1000, 0, 0);
        string[] keys = ["CH6", "CH5"];
        controller.Sync(keys, true, "CH6");
        controller.Arrange(data, keys);
        var axis = controller.Axes["CH6"];
        var other = controller.Axes["CH5"];
        double min = axis.Min, max = axis.Max, otherMin = other.Min, otherMax = other.Max;
        Check(min < 12 && max > 28, "Temperature scaling and arrange cover visible samples");
        controller.Zoom("CH6", .5, 20);
        Check(axis.Min == 20 + (min - 20) * .5 && axis.Max == 20 + (max - 20) * .5 && controller.ManualChannels.Contains("CH6"), "Zoom preserves anchor and marks manual");
        Check(other.Min == otherMin && other.Max == otherMax, "Zoom isolates selected channel");
        min = axis.Min;
        max = axis.Max;
        controller.Pan("CH6", min, max, .25);
        Check(axis.Min == min + (max - min) * .25, "Drag translates original limits");
        min = axis.Min;
        max = axis.Max;
        controller.Arrange(data, keys, true);
        controller.ExpandAutomatic(data, keys);
        controller.Sync(["CH5"], true, "CH5");
        controller.Sync(keys, false, "CH5");
        controller.ApplyPalette(false);
        Check(axis.Min == min && axis.Max == max && !axis.IsVisible && other.IsVisible, "Automatic expansion, visibility and theme preserve manual limits");
        controller.Zoom("CH5", .8, 3);
        otherMin = other.Min;
        otherMax = other.Max;
        controller.RestoreAutomatic(data, keys, "CH6");
        Check(!controller.ManualChannels.Contains("CH6") && controller.ManualChannels.Contains("CH5") && other.Min == otherMin && other.Max == otherMax, "Restore auto affects only requested channel");
        controller.Fit(data, "CH6");
        Check(Math.Abs(axis.Min - 10.4) < 1e-8 && Math.Abs(axis.Max - 29.6) < 1e-8, "Fit uses raw data in visible time range and existing padding");
        controller.Arrange(data, keys);
        Check(controller.ManualChannels.Count == 0, "Arrange resets manual modes");
        controller.SetRange(100, 101, 1000);
        Check(controller.Right - controller.Left == 60, "Minimum time span");
        double left = controller.Left;
        controller.SetRange(double.NaN, 200, 1000);
        Check(controller.Left == left, "Invalid range leaves limits unchanged");
        controller.Sync([], true, null);
        Check(!plot.Axes.GetYAxes().Any(a => controller.Axes.Values.Contains(a)), "Hidden channel axes leave layout");
        Console.WriteLine("PASS axis controller: arrange, scale, pan, zoom, manual preservation, single-axis restore, fit, range guards, hidden axes");
    }
}
