using System.Diagnostics;
using System.Text.Json;

namespace BlueforsMonitor;

public sealed partial class MainWindow
{
    internal async Task CheckPackage(string output, string logs, Stopwatch startup)
    {
        double opened = startup.Elapsed.TotalMilliseconds;
        _scanTimer.Stop();
        _cursorTimer.Stop();
        await OpenLogs(logs);
        if (!_session.Store.HasData || _session.Data is not { Count: > 0 })
            throw new InvalidOperationException("Package did not load log data: " + _viewModel.Status);
        _plot.Plot.GetImage(1000, 600); // Include actual ScottPlot/Skia rendering, not only preparation.
        double firstPlot = startup.Elapsed.TotalMilliseconds;
        var bounds = _session.Store.Snapshot.Bounds;
        Navigate(bounds[0], bounds[1], true, false);
        if (LastLoad != null)
            await LastLoad;
        if (_session.Data is not { Count: > 0, Resolution: > 0 })
            throw new InvalidOperationException("Overview cache did not load");
        foreach (string theme in new[] { "浅色", "深色" })
        {
            SetTheme(theme);
            Arrange();
            _plot.Plot.SavePng(Path.Combine(output, theme == "浅色" ? "light.png" : "dark.png"), 1000, 600);
        }
        SetTheme("跟随系统");
        _settings.Save();
        var restored = AppSettings.Load();
        if (restored.Theme != "跟随系统" || restored.LogRoot != logs)
            throw new InvalidOperationException("Settings roundtrip failed");
        var snapshot = new DataStore(_session.Store.Folder).Snapshot;
        if (snapshot.Events.Length != _session.Store.Snapshot.Events.Length || snapshot.Counts["CH6"] == 0)
            throw new InvalidOperationException("Event snapshot roundtrip failed");
        if (snapshot.Events.Length == 0)
            throw new InvalidOperationException("Fixture must exercise event detection");
        await ShowEvent(snapshot.Events[0]);
        if (_session.Data is not { Count: > 0 })
            throw new InvalidOperationException("Event navigation failed");
        _plot.Plot.SavePng(Path.Combine(output, "event.png"), 1000, 600);
        if (_eventList.SelectedEvent != snapshot.Events[0])
            throw new InvalidOperationException("Event selection binding failed");
        _axisController.Zoom("CH6", .5, .5);
        var range = _axisController.Axes["CH6"].Range;
        double beforeMin = range.Min, beforeMax = range.Max;
        Arrange(true);
        if (!_axisController.ManualChannels.Contains("CH6") || range.Min != beforeMin || range.Max != beforeMax)
            throw new InvalidOperationException("Automatic arrangement changed manual scale");
        Arrange();
        if (_axisController.ManualChannels.Count != 0)
            throw new InvalidOperationException("Arrange did not restore automatic axes");
        _dateRange.SetRange(bounds[0], bounds[0] + 3600);
        _dateRange.Apply();
        if (LastLoad != null)
            await LastLoad;
        if (_axisController.Left != bounds[0] || _dateRange.ValidationMessage != null)
            throw new InvalidOperationException("Date range interaction failed");
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
        using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(1460, 980)))
        {
            bitmap.Render(this);
            bitmap.Save(Path.Combine(output, "window.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        using var stream = File.Create(Path.Combine(output, "report.json"));
        using var json = new Utf8JsonWriter(stream, new()
        {
            Indented = true
        });
        json.WriteStartObject();
        json.WriteString("version", AppVersion.Current);
        json.WriteBoolean("dynamic_code", System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported);
        json.WriteNumber("window_opened_ms", opened);
        json.WriteNumber("first_plot_rendered_ms", firstPlot);
        json.WriteNumber("working_set_bytes", Process.GetCurrentProcess().WorkingSet64);
        json.WriteNumber("samples", snapshot.Counts.Values.Sum());
        json.WriteNumber("events", snapshot.Events.Length);
        json.WriteString("checks", "logs, SQLite overview, settings, events, themes, PNG, event navigation, manual axes, date range");
        json.WriteEndObject();
    }
}
