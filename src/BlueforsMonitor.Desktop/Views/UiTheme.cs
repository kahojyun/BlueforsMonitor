using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace BlueforsAvalonia;

public static class UiTheme
{
    public static string Font => OperatingSystem.IsWindows() ? "Microsoft YaHei" : OperatingSystem.IsMacOS() ? "PingFang SC" : "Noto Sans CJK SC";
    public static readonly Dictionary<string, (string Dark, string Light)> Channels = new()
    {
        ["CH6"] = ("#65d8ce", "#087e83"),
        ["CH5"] = ("#f5b868", "#a45e08"),
        ["Flow"] = ("#bc9af3", "#7851ba"),
        ["P2"] = ("#71b8ed", "#176eaa"),
        ["P3"] = ("#ed8fa9", "#bc426c"),
        ["P4"] = ("#bdd778", "#5b7818"),
        ["CH1"] = ("#e7cc91", "#89702a"),
        ["CH2"] = ("#db9bce", "#a24991"),
        ["P1"] = ("#81cdba", "#26836c"),
        ["P5"] = ("#94a2ed", "#4d5eb6"),
        ["P6"] = ("#df9f82", "#a75e3b")
    };
    public static string Channel(string key, bool dark) => dark ? Channels[key].Dark : Channels[key].Light;
    public static void Install(Application app)
    {
        var dark = new ResourceDictionary();
        var light = new ResourceDictionary();
        void Add(string key, string d, string l)
        {
            dark[key] = Brush.Parse(d);
            light[key] = Brush.Parse(l);
        }
        Add("Page", "#0c1521", "#eef3f7");
        Add("Surface", "#142233", "#ffffff");
        Add("Plot", "#101c2a", "#ffffff");
        Add("Border", "#27394c", "#ced9e4");
        foreach (var hex in new[] { "#97acc2", "#c0d0e0", "#6c94b5", "#91c6e8", "#849ab2", "#6d8299", "#768ea6", "#8096ac", "#647d95", "#8fada7", "#bca582" })
            Add(hex, hex, "#526a7d");
        Add("#e0e9f3", "#e0e9f3", "#203647");
        Add("#edf4fa", "#edf4fa", "#172e40");
        Add("#83bcea", "#83bcea", "#226e9c");
        foreach (var pair in Channels.Values)
            Add(pair.Dark, pair.Dark, pair.Light);
        app.Resources.ThemeDictionaries[ThemeVariant.Dark] = dark;
        app.Resources.ThemeDictionaries[ThemeVariant.Light] = light;
    }
    public static void Foreground(TextBlock control, string resource) => control.Bind(TextBlock.ForegroundProperty, control.GetResourceObservable(resource));
}

public sealed class DateTimeField : StackPanel
{
    public CalendarDatePicker Date { get; } = new() { Width = 142, SelectedDateFormat = CalendarDatePickerFormat.Custom, CustomDateFormatString = "yyyy-MM-dd", FirstDayOfWeek = DayOfWeek.Monday };
    public TimePicker Time { get; } = new() { Width = 94, ClockIdentifier = "24HourClock", MinuteIncrement = 1 };
    public DateTime? Value => Date.SelectedDate is DateTime day && Time.SelectedTime is TimeSpan time ? day.Date + time : null;
    public DateTimeField()
    {
        // Fluent's internal button otherwise keeps its 242px minimum and clips both numbers.
        Time.Resources["TimePickerThemeMinWidth"] = 94d;
        Time.Resources["TimePickerThemeMaxWidth"] = 94d;
        Time.MinHeight = 34;
        Time.FontSize = 12;
        Orientation = Avalonia.Layout.Orientation.Horizontal;
        Spacing = 4;
        Children.Add(Date);
        Children.Add(Time);
    }
    public void Set(double unix)
    {
        var value = Clock.At(unix);
        Date.SelectedDate = value.Date;
        Date.DisplayDate = value.Date;
        Time.SelectedTime = new TimeSpan(value.Hour, value.Minute, 0);
    }
}
