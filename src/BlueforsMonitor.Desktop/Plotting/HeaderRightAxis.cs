namespace BlueforsAvalonia;

/// <summary>Horizontal channel/unit heading above the axis, including PNG exports.</summary>
public sealed class HeaderRightAxis : ScottPlot.AxisPanels.RightAxis
{
    readonly ScottPlot.LabelStyle _header = new() { Alignment = ScottPlot.Alignment.MiddleCenter, FontSize = 10 };
    public ScottPlot.PixelRect HeaderRect => _header.LastRenderPixelRect;
    public override void Render(ScottPlot.RenderPack rp, float size, float offset)
    {
        base.Render(rp, size, offset);
        if (!IsVisible)
            return;
        _header.Text = LabelStyle.Text;
        _header.FontName = UiTheme.PlotFont;
        _header.ForeColor = LabelStyle.ForeColor;
        _header.Render(rp.Canvas, new ScottPlot.Pixel(rp.DataRect.Right + offset + size / 2, rp.DataRect.Top - 13), rp.Paint);
    }
}
