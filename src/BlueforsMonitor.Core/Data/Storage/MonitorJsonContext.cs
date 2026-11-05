using System.Text.Json.Serialization;

namespace BlueforsMonitor;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Snapshot))]
[JsonSerializable(typeof(int[]))]
internal partial class MonitorJsonContext : JsonSerializerContext;
