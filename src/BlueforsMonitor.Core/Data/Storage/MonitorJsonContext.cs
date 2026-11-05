using System.Text.Json.Serialization;

namespace BlueforsAvalonia;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Snapshot))]
[JsonSerializable(typeof(int[]))]
internal partial class MonitorJsonContext : JsonSerializerContext
{
    internal static MonitorJsonContext CaseInsensitive { get; } = new(new() { PropertyNameCaseInsensitive = true, WriteIndented = true });
}
