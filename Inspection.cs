using System.Text.Json;

namespace Tweakly;

public static class Inspection
{
    // Explicit diagnostic command: provider reads only; no journal preparation or setters.
    public static async Task Run(string path)
    {
        var info = new SystemInfo(); var backend = new WindowsBackend(); var operations = new List<Operation>();
        foreach (var id in new[] { "gamemode-on", "cpu-intel", "cpu-amd", "gpu-nvidia", "gpu-amd", "gpu-intel", "nvidia-profile", "ntfs", "tcp", "mld-icmp" })
        {
            var option = Catalog.Get(id); if (info.Incompatible(option) is not null) continue;
            var built = await FeatureBuilder.Build(option, [], info);
            operations.AddRange(built.Where(o => o.Kind != "Registry").Concat(built.Where(o => o.Kind == "Registry").Take(2)));
        }
        operations.Add(new() { Kind = "Memory", Target = "MemoryCompression", Value = "false" });
        operations.Add(new() { Kind = "Service", Target = "SysMain", Value = "4" });
        operations.Add(Catalog.Get("telemetry").Operations.First(o => o.Kind == "Task"));
        if (info.Adapters.Count > 0)
        {
            var inputs = new Dictionary<string, string> { ["adapter"] = info.Adapters[0].Id, ["mtu"] = "1500" };
            operations.AddRange((await FeatureBuilder.Build(Catalog.Get("mtu"), inputs, info)).Take(2));
        }
        var rows = new List<object>();
        foreach (var op in operations.DistinctBy(o => o.Key))
        {
            var value = await backend.Read(op); rows.Add(new { op.Kind, op.Target, op.Name, value.Supported, value.Exists, value.Reason, value.RequiresElevation });
        }
        File.WriteAllText(path, JsonSerializer.Serialize(new { ReadOnly = true, info.Os, info.Build, Providers = rows }, Catalog.Json));
    }
}
