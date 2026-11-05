using System.IO.Compression;
using System.Text;
using BlueforsAvalonia;

static class JsonCompatibilityChecks
{
    public static void Run(string scratch)
    {
        var root = Path.Combine(scratch, "json-logs");
        Directory.CreateDirectory(root);
        var store = LogImporter.OpenDirectory(root, Path.Combine(scratch, "json-cache"));
        // Literal pre-AOT wire formats: do not generate fixtures using the new serializer.
        File.WriteAllText(Path.Combine(store.Folder, "events.json"), """
            {"source":"旧日志","timezone":"Asia/Shanghai","detection":"legacy","detectorsha256":"old",
             "bounds":[60,120],"counts":{"CH6":2},"events":[{"start":60,"peak":90,"end":120,
             "baseline":0.012,"value":0.022,"fast":true,"recovered":true,"detail":"中文事件"}]}
            """);
        var reopened = new DataStore(store.Folder);
        Verification.Check(reopened.Snapshot.Events.Single().Detail == "中文事件" && reopened.Snapshot.Events[0].Value == .022, "Legacy case-insensitive event snapshot");
        reopened.SaveSnapshot();
        Verification.Check(new DataStore(store.Folder).Snapshot.Events.SequenceEqual(reopened.Snapshot.Events), "Source-generated snapshot roundtrip");
        using var bytes = new MemoryStream();
        using (var zip = new ZLibStream(bytes, CompressionLevel.Fastest, true))
            zip.Write(Encoding.UTF8.GetBytes("""{"1800":{"CH6":[[[60,0.012],[120,0.022]],[120]]},"21600":{"CH6":[[[60,0.012],[120,0.022]],[120]]}}"""));
        using (var db = store.Open(true))
        using (var command = db.CreateCommand())
        {
            command.CommandText = "INSERT INTO overview(day,payload) VALUES(0,$p)";
            command.Parameters.AddWithValue("$p", bytes.ToArray());
            command.ExecuteNonQuery();
        }
        foreach (double span in new[] { 4 * 86400d, 40 * 86400d })
        {
            var data = store.Read(0, 200, span, CancellationToken.None).Series["CH6"];
            Verification.Check(data.Points.SequenceEqual(new Sample[] { new(60,.012), new(120,.022) }) && data.Gaps.Contains(120), "Pre-AOT compressed overview schema");
        }
        Console.WriteLine("PASS legacy JSON: event snapshot and both compressed overview levels");
    }
}
