using System.Text.Json;

namespace BlueforsAvalonia;

public sealed class AppSettings
{
    public static string? SettingsDirectory
    {
        get; set;
    }
    public LayoutSettings Layout { get; set; } = new();
    public int Version { get; set; } = 1;
    public string Theme { get; set; } = "跟随系统";
    public bool MultipleAxes { get; set; } = true;
    public bool EventBandVisible { get; set; } = true;
    public string? LogRoot
    {
        get; set;
    }
    public string[] VisibleChannels { get; set; } = ["CH6", "CH5", "Flow", "P2", "P3", "P4"];
    public double FollowSeconds { get; set; } = 21600;
    public bool Follow { get; set; } = true;
    public static string Root => SettingsDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlueforsMonitor.Avalonia");
    public static AppSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path.Combine(Root, "settings.json"))) ?? new();
            settings.Layout ??= new();
            settings.Layout.Normalize();
            return settings;
        }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Root);
        string file = Path.Combine(Root, "settings.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(file + ".tmp", file, true);
    }
}
