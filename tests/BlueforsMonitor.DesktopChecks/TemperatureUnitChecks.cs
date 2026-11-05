using BlueforsMonitor;

static class TemperatureUnitChecks
{
    public static void Run()
    {
        using var plot = new ScottPlot.Plot();
        var axes = new AxisController(plot, true);
        string[] keys = DataStore.Keys;
        axes.SetRange(0, 1000, 1000);
        void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
        WindowData Data(double value) => new(DataStore.Keys.ToDictionary(k => k, _ => new SeriesData([new(100, value)], [])), 0, 1000, 0, 0);
        axes.UpdateUnits(Data(.95), keys);
        Check(keys.Where(k => k.StartsWith("CH")).All(k => axes.Unit(k) == "mK"), "First visible samples below 1 K choose mK for every temperature channel");
        Check(axes.Unit("P2") == "原值" && axes.Factor("Flow") == 1, "Pressure and flow remain unscaled");
        axes.Sync(keys, true, "CH6");
        axes.Arrange(Data(.95), keys);
        axes.Zoom("CH6", .8, 950);
        var axis = axes.Axes["CH6"];
        double minK = axis.Min / axes.Factor("CH6"), maxK = axis.Max / axes.Factor("CH6");
        Check(axes.UpdateUnits(Data(1), keys) && axes.Unit("CH6") == "K", "Switch up at exactly 1 K");
        Check(Math.Abs(axis.Min - minK) < 1e-12 && Math.Abs(axis.Max - maxK) < 1e-12 && axes.ManualChannels.Contains("CH6"), "Switch preserves physical manual range");
        Check(!axes.UpdateUnits(Data(.95), keys) && axes.Unit("CH6") == "K", "Hysteresis retains K near threshold");
        Check(!axes.UpdateUnits(Data(.9), keys), "Lower hysteresis boundary retains K");
        Check(axes.UpdateUnits(Data(.899), keys) && axes.Unit("CH6") == "mK", "Switch down below 0.9 K");
        Check(Math.Abs(axis.Min / 1000 - minK) < 1e-12 && Math.Abs(axis.Max / 1000 - maxK) < 1e-12, "Round trip preserves physical range");
        axes.UpdateUnits(Data(.999), keys);
        Check(axes.Unit("CH6") == "mK", "Hysteresis retains mK below upper boundary");
        var empty = new WindowData(DataStore.Keys.ToDictionary(k => k, _ => new SeriesData([], [])), 0, 1000, 0, 0);
        Check(!axes.UpdateUnits(empty, keys) && axes.Unit("CH6") == "mK", "Missing samples retain unit");
        var mixed = new WindowData(DataStore.Keys.ToDictionary(k => k, _ => new SeriesData([new(100, .5), new(800, 2)], [])), 0, 1000, 0, 0);
        axes.SetRange(0, 500, 1000);
        axes.UpdateUnits(mixed, keys);
        Check(axes.Unit("CH6") == "mK", "High samples outside visible time range do not affect unit");
        axes.SetRange(0, 1000, 1000);
        axes.UpdateUnits(mixed, keys);
        Check(axes.Unit("CH6") == "K", "Visible maximum selects K");
        axes.SetRange(0, 2000, 2000);
        Check(!axes.UpdateUnits(Data(.1), keys), "Incomplete old query cannot select new view units");
        axes.SetRange(0, 1000, 1000);
        axes.ResetUnitHistory();
        axes.UpdateUnits(Data(.95), keys);
        Check(axes.Unit("CH6") == "mK", "New source initializes independently of previous hysteresis state");
        PlotController.Render(plot, Data(.5), axes.Axes, keys.ToHashSet(), _ => "#ffffff", axes.Factor, true);
        var curve = plot.GetPlottables<ScottPlot.Plottables.Scatter>().Single(p => ReferenceEquals(p.Axes.YAxis, axis));
        Check(curve.Data.GetScatterPoints().Single().Y == 500, "Rendered values use selected mK factor");
        Check(axis.LabelStyle.Text == "CH6 · mK", "Axis label agrees with rendered values");
        axes.UpdateUnits(Data(2), keys);
        PlotController.Render(plot, Data(2), axes.Axes, keys.ToHashSet(), _ => "#ffffff", axes.Factor, false);
        curve = plot.GetPlottables<ScottPlot.Plottables.Scatter>().Single(p => ReferenceEquals(p.Axes.YAxis, axis));
        Check(curve.Data.GetScatterPoints().Single().Y == 2 && axis.LabelStyle.Text == "CH6 · K", "K rendering and label agree after switch");
        Console.WriteLine("PASS temperature units: all temperature channels, thresholds, hysteresis, visible maxima, gaps, source reset, manual range and rendered values");
    }
}
