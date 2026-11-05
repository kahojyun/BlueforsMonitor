using System.Globalization;

namespace BlueforsMonitor;

// Temperature event thresholds are in kelvin; detection behavior is regression-tested.
public static class EventDetector
{
    public static List<EventRecord> Detect(IReadOnlyList<Sample> points, CancellationToken token = default)
    {
        var found = new List<EventRecord>();
        bool Cold(int index) => points[index].V > 0 && points[index].V < .1;
        for (int first = 0; first < points.Count;)
        {
            token.ThrowIfCancellationRequested();
            int last = first + 1;
            while (last < points.Count && points[last].T - points[last - 1].T is > 0 and <= 180 && Cold(last) == Cold(last - 1))
                last++;
            int count = last - first;
            if (count < 16 || !Cold(first))
            {
                first = last;
                continue;
            }
            double T(int i) => points[first + i].T;
            double V(int i) => points[first + i].V;
            int i = 15;
            while (i < count)
            {
                double baseline = Median(Enumerable.Range(i - 15, 15).Select(V));
                bool fast = V(i) - V(i - 3) >= .005 && T(i) - T(i - 3) <= 200;
                bool slow = i + 2 < count && Math.Min(V(i), Math.Min(V(i + 1), V(i + 2))) >= baseline + .0015;
                if (baseline >= .1 || !(fast || slow))
                {
                    i++;
                    continue;
                }
                int start = i;
                while (start > Math.Max(0, i - 15) && V(start - 1) > baseline + .0003)
                    start--;
                int end = i + 1;
                bool plateau = false;
                while (end < count - 2 && Math.Max(V(end), Math.Max(V(end + 1), V(end + 2))) > baseline + .0005)
                {
                    if (end - i >= 60 && T(end) - T(start) >= 4 * 3600 && end % 10 == 0)
                    {
                        var recent = Enumerable.Range(end - 59, 60).Select(V).ToArray();
                        if (recent.Max() - recent.Min() <= .0008)
                        {
                            plateau = true;
                            break;
                        }
                    }
                    end++;
                }
                bool recovered = end < count - 2 && !plateau;
                if (!recovered && !plateau)
                    end = count - 1;
                int peak = start;
                for (int j = start + 1; j <= end; j++)
                    if (V(j) > V(peak))
                        peak = j;
                bool exitsCold = last < points.Count && !Cold(last);
                if (V(peak) - baseline >= .003 && !(!recovered && exitsCold))
                {
                    for (int j = Math.Max(3, start); j <= peak; j++)
                        fast |= T(j) - T(j - 3) <= 200 && V(j) - V(j - 3) >= .005;
                    found.Add(new(T(start), T(peak), T(end), baseline, V(peak), fast, recovered, ""));
                }
                i = end + 1;
            }
            first = last;
        }
        found.Reverse();
        return found;
    }
    public static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
    public static string Change(Sample[] points, double start, double end)
    {
        var baseline = points.Where(p => p.T >= start - 900 && p.T < start).Select(p => p.V).ToArray();
        var inside = points.Where(p => p.T >= start && p.T <= end).ToArray();
        if (inside.Length == 0)
            return "无记录";
        if (baseline.Length == 0)
            return "缺事前基线";
        double mid = Median(baseline), delta = inside.Select(p => p.V - mid).MaxBy(Math.Abs);
        return (delta >= 0 ? "+" : "") + delta.ToString("G4", CultureInfo.InvariantCulture);
    }
}
