using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls.ApplicationLifetimes;

namespace BlueforsAvalonia;

// Opt-in diagnostics run against the actual shipped binary, with isolated settings.
internal static class PackageCheck
{
    internal static readonly Stopwatch Timer = Stopwatch.StartNew();
    internal static string? DirectoryPath { get; private set; }
    internal static string? Logs { get; private set; }
    internal static void Configure(string[] args)
    {
        if (args.Length == 0 || args[0] != "--package-check") return;
        if (args.Length != 3) throw new ArgumentException("--package-check OUTPUT_DIRECTORY LOGFILES_DIRECTORY");
        DirectoryPath = Path.GetFullPath(args[1]);
        Logs = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(DirectoryPath);
        AppSettings.SettingsDirectory = Path.Combine(DirectoryPath, "settings");
    }
    internal static async Task Run(MainWindow window, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        int exitCode = 0;
        try { await window.CheckPackage(DirectoryPath!, Logs!, Timer); }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(DirectoryPath!, "error.txt"), error.ToString());
            Console.Error.WriteLine(error);
            exitCode = 1;
        }
        finally { lifetime.Shutdown(exitCode); }
    }
}
