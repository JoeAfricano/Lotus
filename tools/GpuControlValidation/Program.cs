using System.Diagnostics;
using System.Text.Json;
using Lotus.Infrastructure.Integrations;

if (!OperatingSystem.IsWindows()) return 1;
var folder = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "artifacts/gpu-validation");
Directory.CreateDirectory(folder);
var control = new AfterburnerControl(Path.Combine(folder, "roundtrip-original.json"));
var before = control.Read();
File.WriteAllText(Path.Combine(folder, "before.json"), JsonSerializer.Serialize(new { control.Available, control.Limits, State = before }, new JsonSerializerOptions { IncludeFields = true }));
Console.WriteLine($"Available: {control.Available}; curve points: {before?.Offsets.Length ?? 0}");
if (!control.Available || before is null) return 2;
if (args.Contains("--candidate"))
{
    if (!control.SaveOriginal()) return 3;
    try
    {
        var index = Array.IndexOf(args,"--candidate");
        if (index+2 >= args.Length || !int.TryParse(args[index+1],out var first) || !int.TryParse(args[index+2],out var second))
            throw new ArgumentException("Use --candidate <mV> <MHz>, or --candidate <core offset MHz> <memory offset MHz> --overclock. Explicit values are required.");
        bool oc=args.Contains("--overclock");
        if (oc ? first is <0 or >450 || second is <0 or >1000 : first is <850 or >1100 || second is <1000 or >3000)
            throw new ArgumentOutOfRangeException(nameof(args),"Candidate outside the validation tool limits.");
        var setting = oc ? new Lotus.Core.Telemetry.GpuSetting(first,second) : new(0,0,first,second);
        var applied = control.Apply(setting);
        var state = control.Read();
        File.WriteAllText(Path.Combine(folder,"candidate.json"),JsonSerializer.Serialize(new { applied, State=state }));
        Console.WriteLine($"Candidate application/readback confirmed: {applied}");
        return applied ? 0 : 4;
    }
    finally
    {
        var recovered = control.RestoreOriginal();
        Console.WriteLine($"Original state restored: {recovered}");
        if (!recovered) throw new InvalidOperationException("Restoration pending; recovery journal retained.");
    }
}
if (!args.Contains("--roundtrip")) return 0;
var expected = control.HasPendingOriginal
    ? JsonSerializer.Deserialize<AfterburnerControl.State>(File.ReadAllText(Path.Combine(folder, "roundtrip-original.json")))! : before;
// Explicitly selected validation only: send the unchanged state through Afterburner, then compare its readback.
if (!control.SaveOriginal()) return 3;
var clock = Stopwatch.StartNew();
var restored = control.RestoreOriginal();
var after = control.Read();
var matches = after is not null && AfterburnerProfileFile.Matches(
    new AfterburnerProfileFile.Values(expected.PowerLimit,expected.CoreBoostKHz,expected.MemBoostKHz,expected.Offsets),
    new AfterburnerProfileFile.Values(after.PowerLimit,after.CoreBoostKHz,after.MemBoostKHz,after.Offsets));
File.WriteAllText(Path.Combine(folder, "roundtrip-result.json"), JsonSerializer.Serialize(new { restored, matches, elapsedMs = clock.ElapsedMilliseconds, State = after }));
Console.WriteLine($"Unchanged-state roundtrip: restored={restored}, matches={matches}, {clock.ElapsedMilliseconds} ms");
return restored && matches ? 0 : 4;
