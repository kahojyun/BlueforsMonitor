using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System.Globalization;

namespace BlueforsAvalonia;

public sealed class CursorOverlay : Control
{
    public bool Dark { get; set; } = true;
    public Rect DataRect
    {
        get; set;
    }
    public double? CursorX
    {
        get; set;
    }
    public double? MarkerX
    {
        get; set;
    }
    public CursorOverlay() => IsHitTestVisible = false;
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (DataRect.Width <= 0)
            return;
        using (context.PushClip(DataRect))
        {
            if (CursorX is double x)
                context.DrawLine(new Pen(Brushes.SlateGray, 1, DashStyle.Dash), new(x, DataRect.Top), new(x, DataRect.Bottom));
            if (MarkerX is double m)
                context.DrawLine(new Pen(Dark ? Brushes.Gold : Brush.Parse("#b67d00"), 1.5), new(m, DataRect.Top), new(m, DataRect.Bottom));
        }
    }
}

public sealed class EventBand : Control
{
    public bool Dark { get; set; } = true;
    public EventRecord[] Events { get; set; } = [];
    public EventRecord? Selected
    {
        get; set;
    }
    public double Left
    {
        get; set;
    }
    public double Right
    {
        get; set;
    }
    public double PlotLeft
    {
        get; set;
    }
    public double PlotRight
    {
        get; set;
    }
    public event Action<EventRecord, bool>? Picked;
    public EventBand()
    {
        Height = 38;
        Cursor = new Cursor(StandardCursorType.Hand);
        PointerPressed += (_, e) =>
        {
            var hit = Hit(e.GetPosition(this).X);
            if (hit != null)
            {
                Picked?.Invoke(hit, e.ClickCount >= 2);
                e.Handled = true;
            }
        };
        PointerMoved += (_, e) =>
        {
            var hit = Hit(e.GetPosition(this).X);
            ToolTip.SetTip(this, hit == null ? "单击选中事件 · 双击回看" : $"{hit.Kind}  {Clock.At(hit.Start):MM-dd HH:mm} – {Clock.At(hit.End):MM-dd HH:mm}\n峰值 {hit.PeakText} mK · {hit.DurationText}");
        };
    }
    double X(double time) => PlotLeft + (time - Left) / (Right - Left) * (PlotRight - PlotLeft);
    EventRecord? Hit(double x) => Right > Left && x >= PlotLeft && x <= PlotRight
        ? Events.Where(e => e.End >= Left && e.Start <= Right && x >= X(e.Start) - 3 && x <= Math.Max(X(e.Start) + 3, X(e.End)) + 3)
            .OrderBy(e => Math.Abs(X(e.Peak) - x)).FirstOrDefault() : null;
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brush.Parse(Dark ? "#101c2a" : "#ffffff"), new Rect(Bounds.Size));
        if (Right <= Left || PlotRight <= PlotLeft)
            return;
        context.DrawLine(new Pen(Brush.Parse(Dark ? "#2b3b50" : "#ccd9e4")), new(PlotLeft, 19), new(PlotRight, 19));
        using (context.PushClip(new Rect(PlotLeft, 2, PlotRight - PlotLeft, 34)))
            foreach (var item in Events.Where(e => e.End >= Left && e.Start <= Right))
            {
                double x = X(item.Start), end = X(item.End);
                var brush = new SolidColorBrush(Color.Parse(item.Fast ? "#f0ad68" : "#70b7ed"));
                var box = new Rect(x, 10, Math.Max(3, end - x), 18);
                context.DrawRectangle(brush, item == Selected ? new Pen(Dark ? Brushes.White : Brush.Parse("#23465e"), 1.5) : null, box, 3, 3);
                context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#17202e")), 1.5), new(X(item.Peak), 11), new(X(item.Peak), 27));
            }
        var label = new FormattedText("事件", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(UiTheme.Font), 10, Brushes.SlateGray);
        context.DrawText(label, new(Math.Max(3, PlotRight + 12), 12));
    }
}
