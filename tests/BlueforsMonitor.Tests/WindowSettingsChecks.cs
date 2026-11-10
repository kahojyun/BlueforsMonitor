namespace BlueforsMonitor;

static class WindowSettingsChecks
{
    public static void Run()
    {
        foreach (var (left, top, width, height, scale) in new[] {
            (0, 0, 1366, 728, 1d), (0, 0, 1920, 1040, 1.25),
            (0, 0, 1920, 1040, 1.5), (-1920, 30, 1920, 1010, 1.5),
            (0, 40, 1280, 680, 2d) })
        {
            var fitted = new WindowSettings { X = 9999, Y = -9999 }.Fit(left, top, width, height, scale);
            Verification.Check(fitted.X >= left && fitted.Y >= top &&
                fitted.X + (fitted.Width + 16) * scale <= left + width &&
                fitted.Y + (fitted.Height + 48) * scale <= top + height,
                "Whole window fits working area, including decorations at different DPI");
        }
        var saved = new WindowSettings { Width = 1100, Height = 700, X = 100, Y = 80, Maximized = true };
        var restored = saved.Fit(0, 0, 1920, 1040, 1);
        Verification.Check(restored.Width == 1100 && restored.Height == 700 && restored.X == 100 && restored.Y == 80 && restored.Maximized,
            "Valid normal bounds and maximized preference are retained");
        new AppSettings { Window = saved }.Save();
        var loaded = AppSettings.Load().Window;
        Verification.Check(loaded.Width == 1100 && loaded.Height == 700 && loaded.X == 100 && loaded.Y == 80 && loaded.Maximized,
            "Window settings JSON roundtrip");
        File.WriteAllText(Path.Combine(AppSettings.Root, "settings.json"), "{\"Version\":1,\"Window\":null}");
        Verification.Check(AppSettings.Load().Window.Width == 1460, "Missing window settings use defaults");
    }
}
