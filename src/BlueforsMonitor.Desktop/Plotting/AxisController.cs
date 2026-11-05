using System.Globalization;
using SColor = ScottPlot.Color;

namespace BlueforsAvalonia;

/// <summary>Owns plot axes, time limits and manual channel modes; no controls, settings or I/O.</summary>
public sealed class AxisController
{
    readonly ScottPlot.Plot _plot;
    readonly Dictionary<string, ScottPlot.AxisPanels.RightAxis> _axes = new();
    readonly HashSet<string> _manual = [];
    readonly Dictionary<string, double> _factors = DataStore.Keys.ToDictionary(k => k, k => k == "CH6" ? 1000d : 1d);
    readonly HashSet<string> _unitInitialized = [];
    public IReadOnlyDictionary<string, ScottPlot.AxisPanels.RightAxis> Axes => _axes;
    public IReadOnlySet<string> ManualChannels => _manual;
    public double Left
    {
        get; private set;
    }
    public double Right
    {
        get; private set;
    }
    public double Factor(string key) => _factors[key];
    public string Unit(string key) => key.StartsWith("CH", StringComparison.Ordinal) ? (Factor(key) == 1000 ? "mK" : "K") : "原值";
    public void ResetUnitHistory() => _unitInitialized.Clear();

    /// <summary>Choose from visible samples in K; rescale limits to preserve physical bounds and manual modes.</summary>
    public bool UpdateUnits(WindowData? data, IEnumerable<string> visible)
    {
        if (data == null || data.Left > Left || data.Right < Right)
            return false;
        bool changed = false;
        foreach (string key in visible)
        {
            if (!key.StartsWith("CH", StringComparison.Ordinal))
                continue;
            double? peak = null;
            foreach (var point in data.Series[key].Points)
                if (point.T >= Left && point.T <= Right && double.IsFinite(point.V))
                    peak = Math.Max(peak ?? 0, Math.Abs(point.V));
            if (peak == null)
                continue; // Gaps do not change the last meaningful unit.
            double previous = Factor(key);
            bool initialized = _unitInitialized.Contains(key);
            double next = !initialized ? (peak < 1 ? 1000 : 1)
                : previous == 1000 ? (peak >= 1 ? 1 : 1000)
                : peak < .9 ? 1000 : 1;
            _unitInitialized.Add(key);
            if (previous == next)
                continue;
            var axis = _axes[key];
            _plot.Axes.SetLimitsY(axis.Min * next / previous, axis.Max * next / previous, axis);
            _factors[key] = next;
            axis.LabelStyle.Text = key + " · " + Unit(key);
            changed = true;
        }
        return changed;
    }
    public AxisController(ScottPlot.Plot plot, bool dark)
    {
        _plot = plot;
        _plot.Axes.Title.MinimumSize = 25;
        for (int i = 0; i < DataStore.Keys.Length; i++)
        {
            var key = DataStore.Keys[i];
            var axis = new HeaderRightAxis();
            _plot.Axes.AddRightAxis(axis);
            axis.Color(SColor.FromHex(UiTheme.Channel(key, dark)));
            axis.LabelStyle.Text = key + " · " + Unit(key);
            axis.LabelStyle.FontSize = 10;
            axis.LabelStyle.FontName = UiTheme.PlotFont;
            axis.LabelStyle.IsVisible = false;
            axis.TickLabelStyle.FontName = UiTheme.PlotFont;
            axis.TickLabelStyle.FontSize = 10;
            axis.MinimumSize = key == "P2" ? 80 : 65;
            axis.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic { TargetTickCount = 4, LabelFormatter = v => v.ToString("G5", CultureInfo.InvariantCulture) };
            _axes[key] = axis;
        }
    }
    public void ApplyPalette(bool dark)
    {
        foreach (var pair in _axes)
            pair.Value.Color(SColor.FromHex(UiTheme.Channel(pair.Key, dark)));
    }
    public void SetDateAxis(bool dark)
    {
        var axis = _plot.Axes.DateTimeTicksBottom();
        axis.Color(SColor.FromHex(UiTheme.Hex("Axis", dark)));
        axis.TickGenerator = new ScottPlot.TickGenerators.DateTimeAutomatic { MaxTickCount = 8, LabelFormatter = d => (Right - Left) > 3 * 86400 ? d.ToString("MM-dd") : d.ToString("MM-dd\nHH:mm") };
        axis.TickLabelStyle.FontSize = 11;
    }
    public void SetRange(double left, double right, double dataSpan)
    {
        if (!double.IsFinite(left) || !double.IsFinite(right) || right <= left)
            return;
        double span = Math.Clamp(right - left, 60, Math.Max(365 * 86400, dataSpan * 1.2));
        double center = (left + right) / 2;
        Left = center - span / 2;
        Right = center + span / 2;
        _plot.Axes.SetLimitsX(Clock.Plot(Left), Clock.Plot(Right));
    }
    public string? AxisAt(ScottPlot.Pixel point)
    {
        foreach (var pair in _axes)
            if (pair.Value.IsVisible && pair.Value is HeaderRightAxis header && header.HeaderRect.Contains(point))
                return pair.Key;
        var axis = _plot.GetAxis(point);
        return _axes.FirstOrDefault(p => ReferenceEquals(p.Value, axis)).Key;
    }
    public void Pan(string key, double originalMin, double originalMax, double fraction)
    {
        double shift = fraction * (originalMax - originalMin);
        _plot.Axes.SetLimitsY(originalMin + shift, originalMax + shift, _axes[key]);
        _manual.Add(key);
    }
    public void RestoreAutomatic(WindowData? data, string[] keys, string? key)
    {
        if (key == null)
        {
            Arrange(data, keys);
            return;
        }
        var keep = new HashSet<string>(_manual);
        _manual.Clear();
        foreach (string other in DataStore.Keys.Where(k => k != key))
            _manual.Add(other);
        Arrange(data, keys, true);
        _manual.Clear();
        foreach (string other in keep.Where(k => k != key))
            _manual.Add(other);
    }
    public void Sync(string[] visible, bool multiple, string? selected)
    {
        // Hidden axes must leave the layout, not just stop painting their ticks.
        foreach (var axis in _axes.Values)
        {
            axis.IsVisible = false;
            _plot.Axes.Remove(axis);
        }
        foreach (var key in visible)
        {
            _axes[key].IsVisible = multiple || key == selected;
            _axes[key].MinimumSize = _axes[key].IsVisible ? (key == "P2" ? 80 : 65) : 0;
            _plot.Axes.AddRightAxis(_axes[key]);
        }
    }
    public void Arrange(WindowData? data, string[] keys, bool keepManual = false)
    {
        if (data is null)
            return;
        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            if (keepManual && _manual.Contains(key))
                continue;
            var ys = data.Series[key].Points.Where(p => p.T >= Left && p.T <= Right).Select(p => p.V * Factor(key)).ToArray();
            double min = ys.Length == 0 ? 0 : ys.Min(), max = ys.Length == 0 ? 1 : ys.Max();
            double height = Math.Max(max - min, Math.Max(Math.Abs((max + min) / 2) * .0002, .000001));
            double total = height * keys.Length * 1.38, center = (max + min) / 2;
            double position = 1 - (i + .5) / keys.Length;
            _plot.Axes.SetLimitsY(center - position * total, center + (1 - position) * total, _axes[key]);
        }
        if (!keepManual)
            _manual.Clear();

    }
    public void ExpandAutomatic(WindowData? data, string[] keys)
    {
        if (data == null)
            return;
        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i];
            if (_manual.Contains(key))
                continue;
            var values = data.Series[key].Points.Where(p => p.T >= Left && p.T <= Right).Select(p => p.V * Factor(key)).ToArray();
            if (values.Length == 0)
                continue;
            var axis = _axes[key];
            double height = axis.Max - axis.Min;
            double low = axis.Min + (1 - (i + .94) / keys.Length) * height, high = axis.Min + (1 - (i + .06) / keys.Length) * height;
            double min = values.Min(), max = values.Max();
            if (min >= low && max <= high)
                continue;
            double total = Math.Max(height, (max - min) * keys.Length * 1.45);
            double center = (max + min) / 2, position = 1 - (i + .5) / keys.Length;
            _plot.Axes.SetLimitsY(center - position * total, center + (1 - position) * total, axis);
        }

    }
    public void Fit(WindowData? data, string? key)
    {
        if (data == null || key == null)
            return;
        var points = data.Series[key].Points.Where(p => p.T >= Left && p.T <= Right).Select(p => p.V * Factor(key)).ToArray();
        if (points.Length == 0)
            return;
        double min = points.Min(), max = points.Max(), pad = Math.Max(max - min, Math.Max(Math.Abs(min) * .0002, .000001)) * .1;
        _plot.Axes.SetLimitsY(min - pad, max + pad, _axes[key]);
        _manual.Remove(key);

    }
    public void Zoom(string key, double factor, double anchor)
    {
        var axis = _axes[key];
        _plot.Axes.SetLimitsY(anchor + (axis.Min - anchor) * factor, anchor + (axis.Max - anchor) * factor, axis);
        _manual.Add(key);

    }
}
