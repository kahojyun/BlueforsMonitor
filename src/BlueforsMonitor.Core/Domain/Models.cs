namespace BlueforsMonitor;

public record Sample(double T, double V);
public record EventRecord(double Start, double Peak, double End, double Baseline, double Value, bool Fast, bool Recovered, string Detail)
{
    public string StartText => Clock.At(Start).ToString("MM-dd HH:mm");
    public string Kind => Fast ? "快速尖峰" : "持续升温";
    public string PeakText => (Value * 1000).ToString("F2");
    public string RiseText => "+" + ((Value - Baseline) * 1000).ToString("F2");
    public string DurationText => ((End - Start) / 60).ToString("F0") + " min";
}
public record Snapshot(int FormatVersion, string Source, string TimeZone, double[] Bounds, Dictionary<string, int> Counts, EventRecord[] Events);
public record SeriesData(Sample[] Points, HashSet<double> Gaps);
public record WindowData(Dictionary<string, SeriesData> Series, double Left, double Right, int Resolution, double Milliseconds)
{
    public int Count => Series.Values.Sum(s => s.Points.Length);
}

public static class Clock
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
    public static DateTime At(double unix) => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds((long)(unix * 1000)), Zone).DateTime;
    public static double Unix(DateTime local) => new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone.GetUtcOffset(local)).ToUnixTimeSeconds();
    // Plot dates are explicitly in the log's timezone, independent of machine settings.
    public static double Plot(double unix) => At(unix).ToOADate();
    public static double FromPlot(double value) => Unix(DateTime.FromOADate(value));
}
