using Core.Runtime;
using Core.Trader.DelphiLive;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Synthetic in-memory codec measurements only: no SQL, broker, journals, host or market calls.
// These are format-shaped objects, NOT valid financial session fixtures or restart qualification.
Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
var assembly = typeof(EngineArchiveCodec).Assembly;
Console.WriteLine(JsonSerializer.Serialize(new {
    Runtime = Environment.Version.ToString(), AssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
    Module = assembly.ManifestModule.ModuleVersionId, Scope = "Synthetic codec only; no SQL, broker or domain transition validation"
}));
foreach (int count in new[] { 32, 2048 })
{
    string json = JsonSerializer.Serialize(new {
        Account = new { Name = "SYNTHETIC", Records = Enumerable.Range(0, count).Select(i => new {
            Sequence = i, Text = new string('x', 256), Quantity = 1, Price = 10.00m
        }).ToArray() }, Memory = new { Revision = count }
    });
    var package = EngineArchiveCodec.Pack(json);
    string restored = EngineArchiveCodec.Restore(package.Manifest, package.Contents);
    if (DelphiLiveSnapshotCodec.Fingerprint(json) != DelphiLiveSnapshotCodec.Fingerprint(restored)) throw new Exception("Round trip failed");
    int levels = 0, visits = 0;
    var seen = new HashSet<string>();
    var pending = EngineArchiveCodec.References(package.Manifest).ToArray();
    while (pending.Length > 0) {
        levels++; visits += pending.Length;
        foreach (var key in pending) seen.Add(key);
        pending = pending.SelectMany(key => EngineArchiveCodec.References(package.Contents[key])).Where(key => !seen.Contains(key)).Distinct().ToArray();
    }
    Console.WriteLine(JsonSerializer.Serialize(new { Records = count, ExpandedUtf8Bytes = Encoding.UTF8.GetByteCount(json),
        ManifestUtf8Bytes = Encoding.UTF8.GetByteCount(package.Manifest), DistinctFragments = package.Contents.Count,
        FragmentUtf8Bytes = package.Contents.Values.Sum(Encoding.UTF8.GetByteCount), ReferenceLevels = levels, visits }));
    Measure("parse", count, () => { using var d = JsonDocument.Parse(json); });
    using var parsed = JsonDocument.Parse(json);
    Measure("serialize_parsed", count, () => _ = JsonSerializer.Serialize(parsed.RootElement));
    Measure("raw_utf8_sha256", count, () => _ = DelphiLiveSnapshotCodec.Hash(json));
    Measure("canonical_fingerprint", count, () => _ = DelphiLiveSnapshotCodec.Fingerprint(json));
    Measure("fragment_hash_and_references", count, () => {
        foreach (var p in package.Contents) {
            if (DelphiLiveSnapshotCodec.Hash(p.Value) != p.Key) throw new Exception("Fragment mismatch");
            _ = EngineArchiveCodec.References(p.Value);
        }
    });
    Measure("restore_once", count, () => _ = EngineArchiveCodec.Restore(package.Manifest, package.Contents));
    Measure("restore_twice", count, () => {
        _ = EngineArchiveCodec.Restore(package.Manifest, package.Contents);
        _ = EngineArchiveCodec.Restore(package.Manifest, package.Contents);
    });
    // Corruption probes must reject changed and absent fragments; no test trading involved.
    var damaged = new Dictionary<string, string>(package.Contents);
    var first = damaged.Keys.First(); damaged[first] += " ";
    MustReject(() => EngineArchiveCodec.Restore(package.Manifest, damaged));
    damaged.Remove(first); MustReject(() => EngineArchiveCodec.Restore(package.Manifest, damaged));
}
Console.WriteLine(JsonSerializer.Serialize(new { CorruptionProbes = 4, Passed = 4,
    PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64 }));

static void Measure(string stage, int records, Action action)
{
    action(); // warm-up; intentionally no forced GC against a running machine
    const int samples = 5;
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    int[] collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
    var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime;
    var timings = new double[samples];
    for (int i = 0; i < samples; i++) { var sw = Stopwatch.StartNew(); action(); timings[i] = sw.Elapsed.TotalMilliseconds; }
    var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
    var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
    Console.WriteLine(JsonSerializer.Serialize(new { Stage = stage, Records = records, Samples = samples,
        MeanMs = timings.Average(), MinMs = timings.Min(), MaxMs = timings.Max(), CpuMsTotal = cpuMs,
        AllocatedBytesPerCall = bytes / samples, GcCounts = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - collections[i]).ToArray() }));
}
static void MustReject(Action action)
{
    try { action(); } catch (InvalidDataException) { return; }
    throw new Exception("Corrupt fragment accepted");
}
