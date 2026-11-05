using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace BlueforsAvalonia;

public static class UiTheme
{
    public static string PlotFont { get; } = ResolvePlotFont();
    static string ResolvePlotFont()
    {
        using var face = SkiaSharp.SKFontManager.Default.MatchCharacter('温');
        return face?.FamilyName ?? Font;
    }
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
    public static readonly IReadOnlyDictionary<string, (string Dark, string Light)> Palette = new Dictionary<string, (string, string)>
    {
        ["Page"] = ("#0c1521", "#eef3f7"),
        ["Surface"] = ("#142233", "#ffffff"),
        ["Plot"] = ("#101c2a", "#ffffff"),
        ["Border"] = ("#27394c", "#ced9e4"),
        ["Text"] = ("#e0e9f3", "#203647"),
        ["Heading"] = ("#edf4fa", "#172e40"),
        ["SecondaryText"] = ("#97acc2", "#526a7d"),
        ["CursorText"] = ("#c0d0e0", "#40596c"),
        ["AccentText"] = ("#91c6e8", "#226e9c"),
        ["EventFast"] = ("#f5b868", "#985500"),
        ["EventSlow"] = ("#83bcea", "#226e9c"),
        ["Recovered"] = ("#8fada7", "#38695d"),
        ["Pending"] = ("#bca582", "#805c24"),
        ["Error"] = ("#ffaaa6", "#b42318"),
        ["Axis"] = ("#91a5ba", "#526a7d"),
        ["MajorGrid"] = ("#243446", "#e3eaf0"),
        ["MinorGrid"] = ("#182638", "#f0f4f7"),
        ["Marker"] = ("#ffd700", "#956600"),
        ["Selection"] = ("#ffffff", "#23465e"),
        ["PeakMarker"] = ("#17202e", "#ffffff")
    };
    public static string Hex(string role, bool dark) => dark ? Palette[role].Dark : Palette[role].Light;
    public static IBrush Paint(string role, bool dark) => Brush.Parse(Hex(role, dark));
    public static string ChannelResource(string key) => "Channel." + key;
    public static void Install(Application app)
    {
        var dark = new ResourceDictionary();
        var light = new ResourceDictionary();
        void Add(string key, (string Dark, string Light) colors)
        {
            dark[key] = Brush.Parse(colors.Dark);
            light[key] = Brush.Parse(colors.Light);
        }
        foreach (var pair in Palette)
            Add(pair.Key, pair.Value);
        foreach (var pair in Channels)
            Add(ChannelResource(pair.Key), pair.Value);
        app.Resources.ThemeDictionaries[ThemeVariant.Dark] = dark;
        app.Resources.ThemeDictionaries[ThemeVariant.Light] = light;
    }
    public static void Foreground(TextBlock control, string resource) => control.Bind(TextBlock.ForegroundProperty, control.GetResourceObservable(resource));
}
