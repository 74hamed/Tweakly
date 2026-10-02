using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tweakly;

public sealed record PowerSnapshot(string Active, bool Exists, string Export);
public static class PowerPlans
{
    public const string TweaklyId = "39d49594-9ecd-4e80-bb3c-7fbb8d39903e";
    public static string GuidFrom(string text) => Regex.Match(text, @"[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}").Value;
    public static async Task<string> Active() => GuidFrom(await ProcessRunner.Tool("powercfg.exe", "/getactivescheme"));
    public static async Task<List<string>> List() => Regex.Matches(await ProcessRunner.Tool("powercfg.exe", "/list"), @"[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}").Select(m => m.Value.ToLowerInvariant()).ToList();
    public static async Task<Reading> Read(Operation op)
    {
        var exists = (await List()).Contains(op.Target.ToLowerInvariant());
        var export = exists ? await Export(op.Target) : "";
        return new(true, exists, JsonSerializer.Serialize(new PowerSnapshot(await Active(), exists, export)));
    }
    public static async Task<string> Export(string guid)
    {
        var path = Path.Combine(Path.GetTempPath(), "Tweakly-" + Guid.NewGuid().ToString("N") + ".pow");
        try { await ProcessRunner.Tool("powercfg.exe", "/export", path, guid); return Convert.ToBase64String(await File.ReadAllBytesAsync(path)); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    public static async Task Import(string guid, string data)
    {
        var path = Path.Combine(Path.GetTempPath(), "Tweakly-" + Guid.NewGuid().ToString("N") + ".pow");
        try { await File.WriteAllBytesAsync(path, Convert.FromBase64String(data)); await ProcessRunner.Tool("powercfg.exe", "/import", path, guid); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    public static async Task Write(Operation op)
    {
        if (op.Delete) { await ProcessRunner.Tool("powercfg.exe", "/delete", op.Target); return; }
        if (!(await List()).Contains(op.Target.ToLowerInvariant()))
        {
            var source = (await List()).Contains("381b4222-f694-41f0-9685-ff5bb260df2e") ? "381b4222-f694-41f0-9685-ff5bb260df2e" : await Active();
            await ProcessRunner.Tool("powercfg.exe", "/duplicatescheme", source, op.Target);
            await ProcessRunner.Tool("powercfg.exe", "/changename", op.Target, "Tweakly Performance", "Independent Tweakly power plan. AC performance; battery-aware DC settings.");
            foreach (var (setting, ac, dc) in new[] { ("PROCTHROTTLEMIN", "100", "5"), ("PROCTHROTTLEMAX", "100", "100"), ("CPMINCORES", "100", "10") })
            {
                await ProcessRunner.Tool("powercfg.exe", "/setacvalueindex", op.Target, "SUB_PROCESSOR", setting, ac);
                await ProcessRunner.Tool("powercfg.exe", "/setdcvalueindex", op.Target, "SUB_PROCESSOR", setting, dc);
            }
        }
        await ProcessRunner.Tool("powercfg.exe", "/setactive", op.Target);
    }
    public static async Task Restore(Operation op, Reading reading)
    {
        var old = JsonSerializer.Deserialize<PowerSnapshot>(reading.Value)!;
        if (old.Active.Length > 0) await ProcessRunner.Tool("powercfg.exe", "/setactive", old.Active);
        var exists = (await List()).Contains(op.Target.ToLowerInvariant());
        if (!old.Exists) { if (exists) await ProcessRunner.Tool("powercfg.exe", "/delete", op.Target); }
        else if (!exists) await Import(op.Target, old.Export);
        else if (op.Kind == "PowerPlanEntry") { await ProcessRunner.Tool("powercfg.exe", "/delete", op.Target); await Import(op.Target, old.Export); }
        if (old.Active.Length > 0) await ProcessRunner.Tool("powercfg.exe", "/setactive", old.Active);
    }
}
