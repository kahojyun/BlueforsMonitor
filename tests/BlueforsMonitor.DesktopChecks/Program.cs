using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
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
                Console.WriteLine("PASS desktop: TableView columns, resize alignment and header isolation, independent controls, bindings, themes, date validation and layout, logs, calendar, axes, events, follow");
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
        var dateRange = w.GetVisualDescendants().OfType<DateRangeControl>().Single();
        var eventList = w.GetVisualDescendants().OfType<EventListControl>().Single();
        var channels = w.GetVisualDescendants().OfType<ChannelPanelControl>().Single();
        int rangeRequests = 0;
        dateRange.RangeApplied += (_, _) => rangeRequests++;
        var isolatedChannels = new ChannelPanelControl();
        int visibilityRequests = 0, axisRequests = 0;
        isolatedChannels.VisibilityChanged += () => visibilityRequests++;
        isolatedChannels.AxisOptionsChanged += () => axisRequests++;
        isolatedChannels.Configure(new[] { "CH6", "P2" }, true);
        Require(visibilityRequests == 0 && axisRequests == 0 && isolatedChannels.VisibleChannels.SequenceEqual(new[] { "CH6", "P2" }), "Initial configuration must be silent and independent of MainWindow");
        isolatedChannels.SetChannelVisible("P2", false);
        Require(visibilityRequests == 1, "One visibility action must emit one notification");
        var apply = dateRange.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "应用时间"));
        void ApplyDates() => apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var model = Field<MonitorViewModel>(w, "_viewModel");
        model.Status = "binding status";
        model.Source = "binding source";
        model.EventCount = "binding events";
        Require(Field<TextBlock>(w, "_status").Text == model.Status &&
            Field<TextBlock>(w, "_sourceInfo").Text == model.Source &&
            eventList.CountText == model.EventCount && eventList.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == model.EventCount), "Compiled bindings must propagate property changes");
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
        var checks = channels.GetVisualDescendants().OfType<CheckBox>().Where(c => DataStore.Keys.Contains(c.Content as string)).ToDictionary(c => (string)c.Content!);
        foreach (string key in new[] { "Flow", "P2", "P3", "P4" })
            checks[key].IsChecked = false;
        await Task.Delay(250);
        Require(plot.Plot.LastRender.DataRect.Width > width + 150, "Hidden axes must release space");
        Require(AppSettings.Load().VisibleChannels.SequenceEqual(channels.VisibleChannels), "Visibility preferences must persist");
        var axisSelector = channels.GetVisualDescendants().OfType<ComboBox>().Single();
        var multiple = channels.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "多纵轴"));
        multiple.IsChecked = false;
        axisSelector.SelectedItem = "CH5";
        var axes = Field<Dictionary<string, ScottPlot.AxisPanels.RightAxis>>(w, "_axes");
        Require(!axes["CH6"].IsVisible && axes["CH5"].IsVisible && !AppSettings.Load().MultipleAxes, "Single-axis selection and persistence");
        multiple.IsChecked = true;
        axisSelector.SelectedItem = "CH6";
        Require(axes["CH6"].IsVisible && axes["CH5"].IsVisible, "Multiple axes restored");
        foreach (var key in channels.VisibleChannels.ToArray())
            checks[key].IsChecked = false;
        Require(channels.VisibleChannels.Length == 0 && channels.GetVisualDescendants().OfType<Border>().Where(b => b.Child is StackPanel).All(b => !b.IsVisible), "All channels can be hidden");
        checks["CH6"].IsChecked = true;
        checks["CH5"].IsChecked = true;
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
        var from = dateRange.Start;
        var to = dateRange.End;
        double start = Clock.Unix(new DateTime(2026, 11, 5, 10, 0, 0));
        from.Set(start);
        to.Set(start + 3600);
        from.Date.IsDropDownOpen = true;
        await Task.Delay(100);
        Require(from.Date.IsDropDownOpen, "Calendar popup");
        from.Date.IsDropDownOpen = false;
        var follow = Field<CheckBox>(w, "_follow");
        bool? originalFollow = follow.IsChecked;
        double originalLeft = Field<double>(w, "_left"), originalRight = Field<double>(w, "_right");
        void Reject(string expected)
        {
            ApplyDates();
            Require(dateRange.ValidationMessage?.Contains(expected) == true, "Specific inline date error: " + expected + " / " + dateRange.ValidationMessage);
            Require(Field<double>(w, "_left") == originalLeft && Field<double>(w, "_right") == originalRight &&
                follow.IsChecked == originalFollow, "Invalid dates must preserve view and follow mode");
            Require(rangeRequests == 0, "Invalid input must not emit navigation requests");
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
        ApplyDates();
        if (w.LastLoad != null)
            await w.LastLoad;
        Require(rangeRequests == 1, "Applying dates must request navigation only once");
        Require(dateRange.ValidationMessage == null && follow.IsChecked == false, "Valid date range clears error and pauses follow");
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
            foreach (var control in new Control[] { from, to, dateRange })
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
        int selectionRequests = 0, replayRequests = 0;
        eventList.SelectionRequested += _ => selectionRequests++;
        eventList.ReplayRequested += _ => replayRequests++;
        var rows = eventList.GetVisualDescendants().OfType<TableView>().Single();
        rows.SelectedItem = store.Snapshot.Events[0];
        Require(selectionRequests == 1 && replayRequests == 0 && ReferenceEquals(Field<EventRecord>(w, "_selected"), rows.SelectedItem), "Selection must reach window exactly once");
        Require(Field<double>(w, "_left") == start, "Selection must not jump");
        var pointer = new Avalonia.Input.Pointer(1, PointerType.Mouse, true);
        var pointerArgs = new PointerEventArgs(InputElement.PointerReleasedEvent, rows, pointer, w, default, 0, new PointerPointProperties(), KeyModifiers.None);
        Require(rows.Columns.Count == 7 && rows.CanUserResizeColumns, "Seven resizable TableView columns");
        var header = rows.GetVisualDescendants().OfType<TableViewColumnHeader>().First(h => h.Column == rows.Columns[0]);
        var grip = header.GetVisualDescendants().OfType<Thumb>().Single();
        double originalWidth = rows.Columns[0].ActualWidth;
        grip.RaiseEvent(new VectorEventArgs { RoutedEvent = Thumb.DragDeltaEvent, Vector = new Vector(48, 0) });
        await Task.Delay(100);
        Require(rows.Columns[0].Width.IsAbsolute && Math.Abs(rows.Columns[0].ActualWidth - originalWidth - 48) < 1, "Dragging the header grip must resize the column");
        foreach (var cell in rows.GetVisualDescendants().OfType<TableViewCell>())
        {
            var matchingHeader = rows.GetVisualDescendants().OfType<TableViewColumnHeader>().Single(h => h.Column == cell.Column);
            Require(Math.Abs(cell.Bounds.Width - matchingHeader.Bounds.Width) < 1 &&
                Math.Abs(cell.TranslatePoint(default, rows)!.Value.X - matchingHeader.TranslatePoint(default, rows)!.Value.X) < 1, "Headers and cells must stay aligned after resizing");
        }
        header.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, pointerArgs));
        grip.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, pointerArgs));
        Require(replayRequests == 0 && Field<double>(w, "_left") == start, "Header and grip double taps must not replay an event");
        var eventRow = rows.GetVisualDescendants().OfType<TableViewRow>().First();
        Require(Equals(ToolTip.GetTip(eventRow), store.Snapshot.Events[0].Detail), "Row tooltip must retain event details");
        eventRow.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, pointerArgs));
        if (w.LastLoad != null)
            await w.LastLoad;
        Require(selectionRequests == 1 && replayRequests == 1, "Replay must not recursively select");
        Require(from.Value == Clock.At(start), "Replay must preserve entered dates");
        double before = Field<double>(w, "_right");
        int requestsBeforeRefresh = selectionRequests;
        double resizedWidth = rows.Columns[0].Width.Value;
        Call(w, "UpdateMetadata");
        Require(rows.Columns[0].Width.Value == resizedWidth, "Refresh must retain resized widths");
        Require(eventList.SelectedEvent?.Start == store.Snapshot.Events[0].Start && selectionRequests == requestsBeforeRefresh, "Refresh must retain selection without user events");
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
