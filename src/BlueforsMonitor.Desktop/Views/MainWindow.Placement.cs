using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace BlueforsMonitor;

public sealed partial class MainWindow
{
    bool _placementReady;
    readonly DispatcherTimer _placementTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    void InitializeWindowPlacement()
    {
        MinWidth = 1;
        MinHeight = 1;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ApplyInitialPlacement(false);
        _placementTimer.Tick += (_, _) => { _placementTimer.Stop(); RememberNormalBounds(); };
        SizeChanged += (_, _) => QueuePlacement();
        PositionChanged += (_, _) => QueuePlacement();
        PropertyChanged += (_, e) =>
        {
            if (_placementReady && e.Property == WindowStateProperty && WindowState != WindowState.Minimized)
                _settings.Window.Maximized = WindowState == WindowState.Maximized;
        };
        Closed += (_, _) => { _placementReady = false; _placementTimer.Stop(); };
        Closing += (_, _) =>
        {
            _placementTimer.Stop();
            RememberNormalBounds();
            _settings.Save();
        };
    }

    void ApplyInitialPlacement(bool measuredFrame)
    {
        var saved = _settings.Window;
        var screen = saved.X is int x && saved.Y is int y ? Screens.ScreenFromPoint(new PixelPoint(x, y)) : null;
        screen ??= Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null)
        {
            Width = 1000;
            Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }
        var area = screen.WorkingArea;
        double frameWidth = 16, frameHeight = 48;
        if (measuredFrame && FrameSize is Size frame)
        {
            frameWidth = Math.Max(0, frame.Width - ClientSize.Width);
            frameHeight = Math.Max(0, frame.Height - ClientSize.Height);
        }
        var fitted = saved.Fit(area.X, area.Y, area.Width, area.Height, screen.Scaling, frameWidth, frameHeight);
        MinWidth = Math.Min(800, fitted.Width);
        MinHeight = Math.Min(480, fitted.Height);
        Width = fitted.Width;
        Height = fitted.Height;
        Position = new PixelPoint(fitted.X!.Value, fitted.Y!.Value);
    }

    protected override void OnOpened(EventArgs e)
    {
        ApplyInitialPlacement(true);
        bool maximize = _settings.Window.Maximized;
        _placementReady = true;
        RememberNormalBounds();
        if (maximize)
            WindowState = WindowState.Maximized;
        base.OnOpened(e);
    }

    void QueuePlacement()
    {
        if (!_placementReady)
            return;
        _placementTimer.Stop();
        _placementTimer.Start();
    }

    void RememberNormalBounds()
    {
        if (!_placementReady || WindowState != WindowState.Normal || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;
        _settings.Window.Width = ClientSize.Width;
        _settings.Window.Height = ClientSize.Height;
        _settings.Window.X = Position.X;
        _settings.Window.Y = Position.Y;
    }
}
