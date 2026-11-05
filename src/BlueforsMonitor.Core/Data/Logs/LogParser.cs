using System.Globalization;
using System.Text.RegularExpressions;
namespace BlueforsMonitor;

public sealed class LogParser
{
    readonly Dictionary<string, double> _times = [];
    internal static readonly Regex ChannelPattern = new(@"^CH(\d+) T ", RegexOptions.Compiled);
    public List<LogSample> Parse(string name, string line)
    {
        var p = line.Trim().TrimStart('\ufeff').Split(',');
        var output = new List<LogSample>();
        if (p.Length < 3)
            return output;
        string text = p[0].Trim() + " " + p[1].Trim();
        if (!_times.TryGetValue(text, out double stamp))
        {
            if (!DateTime.TryParseExact(text, "dd-MM-yy HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return output;
            stamp = Clock.Unix(date);
            if (_times.Count > 8192)
                _times.Clear();
            _times[text] = stamp;
        }
        void Add(string key, string value)
        {
            if (DataStore.Keys.Contains(key) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number))
                output.Add(new(stamp, key, number));
        }
        var channel = ChannelPattern.Match(name);
        if (channel.Success)
            Add("CH" + channel.Groups[1].Value, p[2]);
        else if (name.StartsWith("Flowmeter", StringComparison.Ordinal))
            Add("Flow", p[2]);
        else if (name.StartsWith("maxigauge", StringComparison.Ordinal))
            for (int i = 2; i < p.Length - 5; i += 6)
                if (p[i + 2].Trim() == "1")
                    Add(p[i + 1].Trim(), p[i + 3]);
        return output;
    }
}
