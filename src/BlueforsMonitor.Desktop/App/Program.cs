using Avalonia;
namespace BlueforsAvalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        PackageCheck.Configure(args);
        return AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}
