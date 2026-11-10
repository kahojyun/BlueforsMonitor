using BlueforsMonitor;
string scratch = Path.Combine(Path.GetTempPath(), "bluefors-tests-" + Guid.NewGuid().ToString("N"));
try
{
    Verification.Check(new AppSettings().Theme == "跟随系统", "Default theme");
    Verification.Check(!new DataStore(scratch).HasData, "Fresh launch must be empty");
    Verification.Check(DataStore.Nearest([new Sample(0, 1)], 91) is null, "Stale cursor sample");
    var reduced = DataStore.Reduce([new(0, 1), new(60, 9), new(120, -2), new(2000, 4)], 1800);
    Verification.Check(reduced.Points.Any(p => p.V == 9) && reduced.Points.Any(p => p.V == -2) && reduced.Gaps.Contains(2000), "Overview extrema and gaps");
    var parser = new LogParser();
    Verification.Check(parser.Parse("CH6 T test.log", "05-11-26,10:00:00,0.012").Single().K == "CH6", "Temperature parser");
    Verification.Check(parser.Parse("Flowmeter test.log", "05-11-26,10:00:00,Infinity").Count == 0, "Finite values only");
    var time = Clock.Unix(new DateTime(2026, 11, 5, 10, 0, 0));
    Verification.Check(Math.Abs(Clock.FromPlot(Clock.Plot(time)) - time) < 1, "Timezone roundtrip");
    AppSettings.SettingsDirectory = Path.Combine(scratch, "settings");
    new AppSettings { Theme = "浅色", LogRoot = "example" }.Save();
    Verification.Check(AppSettings.Load().Theme == "浅色" && AppSettings.Load().LogRoot == "example", "Settings persistence");
    var settingsFile = Path.Combine(AppSettings.Root, "settings.json");
    File.WriteAllText(settingsFile, "{\"Version\":1,\"Theme\":\"浅色\",\"Follow\":false}");
    var migrated = AppSettings.Load();
    Verification.Check(!migrated.Follow && migrated.Theme == "浅色" && migrated.Layout.EventsExpanded && !migrated.Layout.FocusMode, "Partial current settings retain preferences and receive layout defaults");
    migrated.Layout = new LayoutSettings { FocusMode = true, DatesExpanded = true, EventsExpanded = false, DetailsExpanded = false, EventPanelHeight = 230 };
    migrated.Save();
    var restored = AppSettings.Load().Layout;
    Verification.Check(restored.FocusMode && restored.DatesExpanded && !restored.EventsExpanded && !restored.DetailsExpanded && restored.EventPanelHeight == 230, "Layout persistence");
    File.WriteAllText(settingsFile, "{\"Version\":1,\"Layout\":null}");
    Verification.Check(AppSettings.Load().Layout.EventPanelHeight == 150, "Null layout fallback");
    File.WriteAllText(settingsFile, "{\"Version\":1,\"Layout\":{\"EventPanelHeight\":-10}}");
    Verification.Check(AppSettings.Load().Layout.EventPanelHeight == 120, "Invalid panel height is clamped");
    File.WriteAllText(settingsFile, "{\"Version\":999,\"Theme\":\"浅色\"}");
    Verification.Check(AppSettings.Load().Theme == "跟随系统", "Unknown settings format falls back safely");
    File.WriteAllText(settingsFile, "{\"Theme\":\"浅色\"}");
    Verification.Check(AppSettings.Load().Theme == "跟随系统", "Unversioned preview settings are not imported");
    WindowSettingsChecks.Run();
    BackendVerification.Run(scratch);
    StorageChecks.Run(scratch);
    StorageFormatChecks.Run(scratch);
    await MonitoringSessionChecks.Run(scratch);
    Console.WriteLine("All regression checks passed.");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
namespace BlueforsMonitor
{
    public static class Verification
    {
        public static void Check(bool value, string message)
        {
            if (!value)
                throw new InvalidOperationException(message);
        }
    }
}
