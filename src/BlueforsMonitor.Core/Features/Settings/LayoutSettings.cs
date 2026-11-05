namespace BlueforsMonitor;

public sealed class LayoutSettings
{
    public bool FocusMode
    {
        get; set;
    }
    public bool DatesExpanded
    {
        get; set;
    }
    public bool EventsExpanded { get; set; } = true;
    public bool DetailsExpanded { get; set; } = true;
    public double EventPanelHeight { get; set; } = 150;
    public void Normalize() => EventPanelHeight = double.IsFinite(EventPanelHeight)
        ? Math.Clamp(EventPanelHeight, 120, 500) : 150;
}
