using Microsoft.Win32;
using WinRegistry = Microsoft.Win32.Registry;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tweakly;

public sealed record Choice(string Value, string Label) { public override string ToString() => Label; }
public sealed record StartupItem(string Id, string Label, Operation Operation);
public static class FeatureBuilder
{
    public static readonly Dictionary<string, string> InboxApps = new()
    {
        ["Microsoft.BingWeather"] = "Weather", ["Microsoft.GetHelp"] = "Get Help", ["Microsoft.Getstarted"] = "Tips", ["Microsoft.Messaging"] = "Messaging", ["Microsoft.Microsoft3DViewer"] = "3D Viewer", ["Microsoft.MicrosoftSolitaireCollection"] = "Solitaire", ["Microsoft.MicrosoftStickyNotes"] = "Sticky Notes", ["Microsoft.MixedReality.Portal"] = "Mixed Reality", ["Microsoft.OneConnect"] = "Mobile Plans", ["Microsoft.People"] = "People", ["Microsoft.Print3D"] = "Print 3D", ["Microsoft.SkypeApp"] = "Skype", ["Microsoft.WindowsAlarms"] = "Clock", ["Microsoft.WindowsCamera"] = "Camera", ["microsoft.windowscommunicationsapps"] = "Mail / Calendar", ["Microsoft.WindowsMaps"] = "Maps", ["Microsoft.WindowsFeedbackHub"] = "Feedback Hub", ["Microsoft.WindowsSoundRecorder"] = "Sound Recorder", ["Microsoft.YourPhone"] = "Phone Link", ["Microsoft.ZuneMusic"] = "Music", ["Microsoft.ZuneVideo"] = "Movies / TV", ["Microsoft.HEIFImageExtension"] = "HEIF images", ["Microsoft.WebMediaExtensions"] = "Web media", ["Microsoft.WebpImageExtension"] = "WebP images", ["Microsoft.3DBuilder"] = "3D Builder", ["Microsoft.BingNews"] = "News", ["Microsoft.BingFinance"] = "Finance", ["Microsoft.BingSports"] = "Sports", ["Microsoft.CommsPhone"] = "Phone", ["Drawboard.DrawboardPDF"] = "Drawboard PDF", ["Microsoft.Office.Sway"] = "Sway", ["Microsoft.WindowsPhone"] = "Windows Phone"
    };
    public static readonly string[] Regions = ["eu", "nae", "nac", "naw", "br", "asia", "me", "oce"];
    public static bool Maintenance(Tweak tweak) => new[] { "OpenApps", "Bufferbloat", "Ping", "DiskCleanup", "SystemRestore", "RestorePoint", "OptimizeDrive", "RemoveApps", "RemoveCopilot", "RemoveEdge", "InstallEdge", "RegisterApps", "DeviceClean", "DefaultPower" }.Contains(tweak.Handler);
    public static bool ReadOnly(Tweak tweak) => new[] { "OpenApps", "Bufferbloat", "Ping", "DiskCleanup", "SystemRestore" }.Contains(tweak.Handler);
    public static Operation Registry(string target, string name, string value, string type = "REG_DWORD") => new() { Kind = "Registry", Target = target, Name = name, Value = value, Type = type };
    public static async Task<List<Operation>> Build(Tweak tweak, Dictionary<string, string> inputs, SystemInfo info)
    {
        if (info.Incompatible(tweak) is string incompatibility) throw new InvalidOperationException(incompatibility);
        var operations = tweak.Operations.ToList();
        switch (tweak.Handler)
        {
            case "PowerPlan": operations.Add(new() { Kind = "PowerPlan", Target = PowerPlans.TweaklyId, Value = PowerPlans.TweaklyId }); break;
            case "DeletePowerPlans":
                var active = await PowerPlans.Active(); var schemes = await PowerPlans.List();
                foreach (var guid in new[] { "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "381b4222-f694-41f0-9685-ff5bb260df2e", "a1841308-3541-4fab-bc81-f71556f20b4a" })
                    if (schemes.Contains(guid) && !guid.Equals(active, StringComparison.OrdinalIgnoreCase)) operations.Add(new() { Kind = "PowerPlanEntry", Target = guid, Delete = true });
                break;
            case "Ram":
                var ram = Input(inputs, "ram");
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Tweakly.Data.ram.json")!)
                {
                    var templates = JsonSerializer.Deserialize<Dictionary<string, List<Operation>>>(stream, Catalog.Json)!;
                    operations.AddRange(templates.TryGetValue(ram, out var template) ? template : throw new ArgumentException("Select a supported RAM capacity."));
                }
                operations.Add(new() { Kind = "Service", Target = "SysMain", Value = "4" }); break;
            case "DataQueue":
                var queue = Input(inputs, "queue"); if (!new[] { "19", "24", "36" }.Contains(queue)) throw new ArgumentException("Invalid data queue.");
                operations.Add(Registry(@"HKLM\SYSTEM\CurrentControlSet\Services\mouclass\Parameters", "MouseDataQueueSize", queue));
                operations.Add(Registry(@"HKLM\SYSTEM\CurrentControlSet\Services\kbdclass\Parameters", "KeyboardDataQueueSize", queue)); break;
            case "DiskCache":
                var disks = await DeviceChoices("DiskDrive", presentOnly: true);
                var disk = Input(inputs, "disk"); if (!disks.Any(d => d.Value == disk)) throw new ArgumentException("Select a detected disk.");
                operations.Add(Registry(@"HKLM\SYSTEM\CurrentControlSet\Enum\" + disk + @"\Device Parameters\Disk", "UserWriteCacheSetting", "1"));
                operations.Add(Registry(@"HKLM\SYSTEM\CurrentControlSet\Enum\" + disk + @"\Device Parameters\Disk", "CacheIsPowerProtected", "1")); break;
            case "HddParking": operations.AddRange(FindServiceValues("EnableHDDParking", "0")); break;
            case "Mtu":
                var adapter = Adapter(inputs, info); var mtu = Input(inputs, "mtu");
                if (!int.TryParse(mtu, out var bytes) || bytes < 1280 || bytes > 1500) throw new ArgumentException("MTU must be between 1280 and 1500.");
                operations.Add(new() { Kind = "Mtu", Target = adapter.Name, Name = "IPv4", Value = mtu });
                operations.Add(new() { Kind = "Mtu", Target = adapter.Name, Name = "IPv6", Value = mtu });
                operations.Add(new() { Kind = "Tcp", Target = "CongestionProvider", Value = "CTCP" }); break;
            case "AdapterOffload":
                var nic = Adapter(inputs, info);
                foreach (var name in new[] { "*LsoV2IPv4", "*LsoV2IPv6", "*IPChecksumOffloadIPv4", "*TCPChecksumOffloadIPv4", "*TCPChecksumOffloadIPv6", "*UDPChecksumOffloadIPv4", "*UDPChecksumOffloadIPv6" })
                    operations.Add(new() { Kind = "AdapterOffload", Target = nic.Name, Name = name, Value = "0" }); break;
            case "Qos":
                var policy = Input(inputs, "policy"); var application = Input(inputs, "application");
                if (!Regex.IsMatch(policy, @"^[\p{L}\p{N} _.-]{1,60}$")) throw new ArgumentException("Use a policy name of 1–60 letters, numbers, spaces, dots or dashes.");
                if (Path.GetFileName(application) != application || !Regex.IsMatch(application, @"^[^\\/:*?""<>|\r\n]{1,120}\.exe$", RegexOptions.IgnoreCase)) throw new ArgumentException("Select an application EXE filename.");
                var target = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\QoS\" + policy;
                var values = new Dictionary<string, string> { ["Version"] = "1.0", ["Application Name"] = application, ["Protocol"] = "*", ["Local Port"] = "*", ["Local IP"] = "*", ["Local IP Prefix Length"] = "*", ["Remote Port"] = "*", ["Remote IP"] = "*", ["Remote IP Prefix Length"] = "*", ["DSCP Value"] = "46", ["Throttle Rate"] = "-1" };
                foreach (var (name, value) in values) operations.Add(Registry(target, name, value, "REG_SZ"));
                operations.Add(new() { Kind = "Service", Target = "Psched", Value = "1" });
                foreach (var item in info.Adapters) operations.Add(new() { Kind = "Binding", Target = item.Name, Name = "ms_pacer", Value = "true" }); break;
            case "Startup":
                var selected = Input(inputs, "startup"); var items = StartupChoices(info);
                operations.Add(items.SingleOrDefault(s => s.Id == selected)?.Operation ?? throw new ArgumentException("Select a startup entry.")); break;
            case "Updates":
                foreach (var name in new[] { "wuauserv", "UsoSvc", "DoSvc" }) operations.Add(new() { Kind = "Service", Target = name, Value = "4" });
                operations.Add(Registry(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", "1")); break;
            case "EnableUpdates":
                foreach (var name in new[] { "wuauserv", "UsoSvc", "DoSvc" }) operations.Add(new() { Kind = "Service", Target = name, Value = "3" });
                operations.Add(Registry(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoUpdate", "0")); break;
            case "Store":
                var off = tweak.Id.EndsWith("off");
                operations.Add(Registry(@"HKLM\SOFTWARE\Policies\Microsoft\WindowsStore", "RemoveWindowsStore", off ? "1" : "0")); break;
            case "Hibernate": operations.Add(new() { Kind = "Hibernate", Value = "0" }); break;
            case "NvProfile":
                operations.Add(new() { Kind = "Nv", Target = "274197361", Value = "1" }); // 0x1057EB71
                operations.Add(new() { Kind = "Nv", Target = "277041154", Value = "0" }); break; // 0x10835002
            case "Wallpaper":
                var wallpaper = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Tweakly", "wallpaper.png");
                operations.Add(new() { Kind = "Wallpaper", Target = info.Sid, Value = wallpaper }); break;
        }
        if (tweak.Id == "nagle")
        {
            var adapter = Adapter(inputs, info);
            var target = @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\" + "{" + adapter.Id.Trim('{', '}') + "}";
            operations.AddRange(new[] { Registry(target, "TcpAckFrequency", "1"), Registry(target, "TCPNoDelay", "1"), Registry(target, "TcpDelAckTicks", "0") });
        }
        if (tweak.Id == "power-saving")
            foreach (var name in new[] { "EnableHIPM", "EnableDIPM" }) operations.AddRange(FindServiceValues(name, "0"));
        var expanded = new List<Operation>();
        List<Choice>? usb = null;
        string? activePlan = null;
        foreach (var original in operations)
        {
            var op = original with { Target = original.Target.Replace("{sid}", info.Sid) };
            if (op.Target.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase)) op = op with { Target = "HKU\\" + info.Sid + op.Target[4..] };
            else if (op.Target.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase)) op = op with { Target = "HKU\\" + info.Sid + "\\" + op.Target[18..] };
            if (op.Target.Contains("{gpu}"))
            {
                foreach (var gpu in info.Gpus.Where(g => g.Vendor == tweak.Vendor)) expanded.Add(op with { Target = op.Target.Replace("{gpu}", gpu.RegistryPath) });
            }
            else if (op.Target.Contains("{nic}"))
            {
                var adapter = Adapter(inputs, info);
                if (tweak.EthernetOnly && !adapter.Ethernet) throw new ArgumentException("This option requires Ethernet.");
                if (adapter.RegistryPath.Length > 0) expanded.Add(op with { Target = op.Target.Replace("{nic}", adapter.RegistryPath) });
            }
            else if (op.Target.Contains("{servicevalue}")) expanded.AddRange(FindServiceValues(op.Name, op.Value));
            else if (op.Target.Contains("{usb}"))
            {
                usb ??= await DeviceChoices("USB", true);
                foreach (var device in usb) expanded.Add(op with { Target = op.Target.Replace("{usb}", device.Value) });
            }
            else
            {
                if (op.Kind == "Power")
                {
                    activePlan ??= Regex.Match(await ProcessRunner.Tool("powercfg.exe", "/getactivescheme"), @"[a-fA-F0-9]{8}(?:-[a-fA-F0-9]{4}){3}-[a-fA-F0-9]{12}").Value;
                    if (activePlan.Length == 0) throw new InvalidOperationException("Cannot identify the active power plan.");
                    op = op with { Type = op.Type + "|" + activePlan };
                }
                if (op.Kind == "Registry" && !op.Delete) _ = WindowsBackend.Expected(op);
                expanded.Add(op);
            }
        }
        return expanded.DistinctBy(o => o.Key).ToList();
    }
    public static string Input(Dictionary<string, string> inputs, string name) => inputs.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Choose / enter " + name + ".");
    public static Adapter Adapter(Dictionary<string, string> inputs, SystemInfo info) => info.Adapters.SingleOrDefault(a => a.Id == Input(inputs, "adapter")) ?? throw new ArgumentException("Select a detected adapter.");
    private static IEnumerable<Operation> FindServiceValues(string name, string value)
    {
        const string path = @"SYSTEM\CurrentControlSet\Services";
        using var services = WinRegistry.LocalMachine.OpenSubKey(path);
        if (services is null) return [];
        var results = new List<Operation>();
        void Walk(RegistryKey key, string current, int depth)
        {
            if (depth > 5) return;
            try
            {
                if (key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) results.Add(Registry("HKLM\\" + current, name, value));
                foreach (var child in key.GetSubKeyNames()) { using var sub = key.OpenSubKey(child); if (sub is not null) Walk(sub, current + "\\" + child, depth + 1); }
            }
            catch (System.Security.SecurityException) { }
        }
        Walk(services, path, 0); return results;
    }
    public static List<StartupItem> StartupChoices(SystemInfo info)
    {
        var result = new List<StartupItem>();
        foreach (var (hive, prefix) in new[] { (WinRegistry.Users, info.Sid + @"\Software\Microsoft\Windows\CurrentVersion\Run"), (WinRegistry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"), (WinRegistry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run") })
        {
            using var key = hive.OpenSubKey(prefix);
            foreach (var name in key?.GetValueNames() ?? [])
            {
                var target = (hive == WinRegistry.Users ? "HKU\\" : "HKLM\\") + prefix;
                result.Add(new(target + "|" + name, name + (hive == WinRegistry.Users ? " · User" : " · System"), new() { Kind = "Registry", Target = target, Name = name, Delete = true }));
            }
        }
        foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
            if (Directory.Exists(folder)) foreach (var file in Directory.EnumerateFiles(folder, "*.lnk")) result.Add(new(file, Path.GetFileNameWithoutExtension(file) + " · Shortcut", new() { Kind = "StartupFile", Target = file, Delete = true }));
        return result;
    }
    public static async Task<List<Choice>> DeviceChoices(string deviceClass, bool presentOnly)
    {
        var script = presentOnly ? $"@(Get-PnpDevice -PresentOnly -Class {ProcessRunner.Quote(deviceClass)} | Select-Object InstanceId,FriendlyName) | ConvertTo-Json -Compress" : "@(Get-PnpDevice | Where-Object { $_.Present -eq $false } | Select-Object InstanceId,FriendlyName) | ConvertTo-Json -Compress";
        var json = await ProcessRunner.PowerShell(script);
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json);
        return document.RootElement.EnumerateArray().Select(e => new Choice(e.GetProperty("InstanceId").GetString()!, (e.TryGetProperty("FriendlyName", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null) ?? e.GetProperty("InstanceId").GetString()!)).ToList();
    }
}
