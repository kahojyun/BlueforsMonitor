using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using BlueforsAvalonia;
using ScottPlot.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppSettings.SettingsDirectory = Path.Combine(Path.GetTempPath(), "bluefors-ui-" + Guid.NewGuid().ToString("N"));
        return AppBuilder.Configure<CheckApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
public sealed class CheckApp : App
{
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var window = (MainWindow)lifetime.MainWindow!;
        window.Opened += async (_, _) =>
        {
            int result = 0;
            try
            {
                await Check(window);
                Console.WriteLine("PASS desktop: compiled bindings, themes, date validation and layout, logs, calendar, axes, events, follow");
            }
            catch (Exception error) { Console.Error.WriteLine(error); result = 1; }
            finally { lifetime.Shutdown(result); }
        };
    }
    static T Field<T>(MainWindow w, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
    static object? Call(MainWindow w, string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, args);
    static void Require(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }
    static async Task Check(MainWindow w)
    {
        var model = Field<MonitorViewModel>(w, "_viewModel");
        model.Status = "binding status";
        model.Source = "binding source";
        model.EventCount = "binding events";
        Require(Field<TextBlock>(w, "_status").Text == model.Status &&
            Field<TextBlock>(w, "_sourceInfo").Text == model.Source &&
            Field<TextBlock>(w, "_eventCount").Text == model.EventCount, "Compiled bindings must propagate property changes");
        Require(!Field<DataStore>(w, "_store").HasData, "First launch must be empty");
        string logs = Path.Combine(AppSettings.Root, "logs");
        Directory.CreateDirectory(logs);
        string file = Path.Combine(logs, "CH6 T 26-11-05.log");
        File.WriteAllLines(file, Enumerable.Range(0, 60).Select(i => $"05-11-26,10:{i:00}:00,{(i >= 20 && i <= 22 ? "0.028" : "0.012")}"));
        await (Task)Call(w, "OpenLogs", logs)!;
        var store = Field<DataStore>(w, "_store");
        Require(store.Snapshot.Events.Length == 1, "Event indexing");
        var plot = Field<AvaPlot>(w, "_plot");
        await Task.Delay(250);
        float width = plot.Plot.LastRender.DataRect.Width;
        var checks = Field<Dictionary<string, CheckBox>>(w, "_checks");
        foreach (string key in new[] { "Flow", "P2", "P3", "P4" })
            checks[key].IsChecked = false;
        await Task.Delay(250);
        Require(plot.Plot.LastRender.DataRect.Width > width + 150, "Hidden axes must release space");
        var theme = Field<ComboBox>(w, "_theme");
        theme.SelectedItem = "浅色";
        await Task.Delay(100);
        Require(!Field<bool>(w, "_dark"), "Light theme");
        theme.SelectedItem = "深色";
        await Task.Delay(100);
        Require(Field<bool>(w, "_dark"), "Dark theme");
        theme.SelectedItem = "跟随系统";
        Require(w.RequestedThemeVariant == ThemeVariant.Default, "System theme preference");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        await Task.Delay(100);
        Require(!Field<bool>(w, "_dark"), "Inherited light theme");
        Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        await Task.Delay(100);
        Require(Field<bool>(w, "_dark"), "Inherited dark theme");
        Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        var from = Field<DateTimeField>(w, "_from");
        var to = Field<DateTimeField>(w, "_to");
        double start = Clock.Unix(new DateTime(2026, 11, 5, 10, 0, 0));
        from.Set(start);
        to.Set(start + 3600);
        from.Date.IsDropDownOpen = true;
        await Task.Delay(100);
        Require(from.Date.IsDropDownOpen, "Calendar popup");
        from.Date.IsDropDownOpen = false;
        var errorLabel = Field<TextBlock>(w, "_dateError");
        var follow = Field<CheckBox>(w, "_follow");
        bool? originalFollow = follow.IsChecked;
        double originalLeft = Field<double>(w, "_left"), originalRight = Field<double>(w, "_right");
        void Reject(string expected)
        {
            Call(w, "ApplyDates");
            Require(errorLabel.IsVisible && errorLabel.Text!.Contains(expected), "Specific inline date error: " + expected + " / " + errorLabel.Text);
            Require(Field<double>(w, "_left") == originalLeft && Field<double>(w, "_right") == originalRight &&
                follow.IsChecked == originalFollow, "Invalid dates must preserve view and follow mode");
        }
        from.Date.Text = "";
        Reject("开始时间");
        from.Set(start);
        from.Date.Text = "2026-02-30";
        Reject("有效日期");
        from.Set(start);
        from.Time.SelectedTime = null;
        Reject("请选择时间");
        from.Set(start);
        to.Date.Text = "";
        Reject("结束时间");
        to.Set(start);
        Reject("晚于");
        to.Set(start - 60);
        Reject("晚于");
        to.Set(start + 3600);
        await Task.Delay(50);
        var dateEditor = from.Date.GetVisualDescendants().OfType<TextBox>().Single();
        dateEditor.Focus();
        dateEditor.Text = "2026-02-30";
        to.Date.Focus();
        await Task.Delay(50);
        Reject("开始时间");
        from.Set(start);
        await Task.Delay(50);
        Call(w, "ApplyDates");
        if (w.LastLoad != null)
            await w.LastLoad;
        Require(!errorLabel.IsVisible && follow.IsChecked == false, "Valid date range clears error and pauses follow");
        w.Width = 1280;
        foreach (string mode in new[] { "浅色", "深色" })
        {
            theme.SelectedItem = mode;
            await Task.Delay(150);
            bool dark = mode == "深色";
            var eventLabels = w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == store.Snapshot.Events[0].Kind).ToArray();
            Require(eventLabels.Length > 0, "Event labels must be realized for theme verification");
            foreach (var label in eventLabels)
                Require((label.Foreground as ISolidColorBrush)?.Color == Color.Parse(UiTheme.Hex("EventFast", dark)), "Existing event rows must recolor");
            Require(from.Time.Bounds.Width >= from.Time.DesiredSize.Width - 1 &&
                to.Time.Bounds.Width >= to.Time.DesiredSize.Width - 1, "Native time picker must not be clipped");
            foreach (var control in new Control[] { from, to, errorLabel })
            {
                if (!control.IsVisible)
                    continue;
                var origin = control.TranslatePoint(default, w)!.Value;
                Require(origin.X >= 0 && origin.X + control.Bounds.Width <= w.Bounds.Width, "Date controls must fit minimum window width");
            }
            if (Environment.GetEnvironmentVariable("BLUEFORS_CHECK_SCREENSHOTS") is string directory)
            {
                Directory.CreateDirectory(directory);
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)w.Bounds.Width * 2, (int)w.Bounds.Height * 2), new Vector(192, 192));
                bitmap.Render(w);
                bitmap.Save(Path.Combine(directory, dark ? "dark.png" : "light.png"), PngBitmapEncoderOptions.Default);
            }
        }
        theme.SelectedItem = "跟随系统";

        Require(Field<double>(w, "_left") == start, "Date apply");
        Call(w, "SelectEvent", store.Snapshot.Events[0]);
        Require(Field<double>(w, "_left") == start, "Selection must not jump");
        await (Task)Call(w, "ShowEvent", store.Snapshot.Events[0])!;
        Require(from.Value == Clock.At(start), "Replay must preserve entered dates");
        double before = Field<double>(w, "_right");
        File.AppendAllText(file, "05-11-26,11:00:00,0.013\n");
        await (Task)Call(w, "SyncCurrent", true)!;
        Require(Field<double>(w, "_right") == before, "Paused view moved");
        Field<CheckBox>(w, "_follow").IsChecked = true;
        if (w.LastLoad != null)
            await w.LastLoad;
        File.AppendAllText(file, "05-11-26,11:01:00,0.013\n");
        await (Task)Call(w, "SyncCurrent", true)!;
        Require(Field<double>(w, "_right") == start + 3660, "Follow did not advance");
    }
}
