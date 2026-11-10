namespace BlueforsMonitor;

public sealed class WindowSettings
{
    public double Width { get; set; } = 1460;
    public double Height { get; set; } = 980;
    public int? X
    {
        get; set;
    }
    public int? Y
    {
        get; set;
    }
    public bool Maximized
    {
        get; set;
    }

    // Saved sizes are logical units; screen coordinates/work area are physical pixels.
    public WindowSettings Fit(int left, int top, int pixelWidth, int pixelHeight, double scale,
        double frameWidth = 16, double frameHeight = 48)
    {
        scale = double.IsFinite(scale) && scale > 0 ? scale : 1;
        double availableWidth = Math.Max(1, pixelWidth / scale - 24 - frameWidth);
        double availableHeight = Math.Max(1, pixelHeight / scale - 24 - frameHeight);
        double width = Math.Clamp(double.IsFinite(Width) ? Width : 1460, Math.Min(800, availableWidth), availableWidth);
        double height = Math.Clamp(double.IsFinite(Height) ? Height : 980, Math.Min(480, availableHeight), availableHeight);
        int margin = (int)Math.Ceiling(12 * scale);
        int maxX = Math.Max(left + margin, left + pixelWidth - margin - (int)Math.Ceiling((width + frameWidth) * scale));
        int maxY = Math.Max(top + margin, top + pixelHeight - margin - (int)Math.Ceiling((height + frameHeight) * scale));
        return new()
        {
            Width = width,
            Height = height,
            X = Math.Clamp(X ?? (left + margin + (maxX - left - margin) / 2), left + margin, maxX),
            Y = Math.Clamp(Y ?? (top + margin + (maxY - top - margin) / 2), top + margin, maxY),
            Maximized = Maximized
        };
    }
}
