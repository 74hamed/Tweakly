using Microsoft.Win32;
using System.Globalization;
using System.Security.AccessControl;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tweakly;

public sealed record RegValue(string Kind, string[] Data);
public sealed record ServiceSnapshot(int Start, RegValue? DelayedAutoStart);
public sealed class RegSnapshot
{
    public bool KeyExisted { get; set; }
    public List<string> MissingParents { get; set; } = [];
    public RegValue? Value { get; set; }
    public RegTree? Tree { get; set; }
}
public sealed class RegTree
{
    public SortedDictionary<string, RegValue> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public SortedDictionary<string, RegTree> Children { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Security { get; set; } = "";
}

public sealed class WindowsBackend : ISystemBackend
{
    public async Task<Reading> Read(Operation operation)
    {
        var op = Normalize(operation);
        try
        {
            switch (op.Kind)
            {
                case "Unsupported": return new(false, false, "", "Legacy switch is unavailable on supported Windows versions.");
                case "Registry": return ReadRegistry(op);
                case "Service":
                    using (var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + op.Target))
                        return service?.GetValue("Start") is object start ? new(true, true, JsonSerializer.Serialize(new ServiceSnapshot(Convert.ToInt32(start), service.GetValueNames().Contains("DelayedAutoStart", StringComparer.OrdinalIgnoreCase) ? ReadValue(service, "DelayedAutoStart") : null))) : new(false, false, "", "Service is not installed.");
                case "Task":
                    return new(true, true, ScheduledTasks.Read(op.Target).ToString().ToLowerInvariant());
                case "Bcd":
                    var bcd = await ProcessRunner.Tool("bcdedit.exe", "/enum", "{current}");
                    var match = Regex.Match(bcd, @"(?im)^\s*" + Regex.Escape(op.Target) + @"\s+(.+)$");
                    return new(true, match.Success, match.Success ? match.Groups[1].Value.Trim() : "");
                case "Power":
                    var query = await ProcessRunner.Tool("powercfg.exe", "/qh", op.Type.Split('|').LastOrDefault() is string guid && guid.Contains('-') ? guid : "SCHEME_CURRENT", op.Target, op.Name);
                    var values = Regex.Matches(query, @"(?im)^.*(?:0x)([a-f0-9]{8})\s*$");
                    if (values.Count < 2) return new(false, false, "", "Power setting is not exposed by this system.");
                    return new(true, true, uint.Parse(values[values.Count - (op.Type.StartsWith("DC") ? 1 : 2)].Groups[1].Value, NumberStyles.HexNumber).ToString(CultureInfo.InvariantCulture));
                case "Fsutil":
                    var fs = await ProcessRunner.Tool("fsutil.exe", "behavior", "query", op.Target);
                    if (op.Target.Equals("DisableDeleteNotify", StringComparison.OrdinalIgnoreCase))
                    {
                        var trim = Regex.Match(fs, @"(?im)^NTFS\s+DisableDeleteNotify\s*=\s*(\d+)");
                        return trim.Success ? new(true, true, trim.Groups[1].Value) : new(false, false, "", "Cannot read the NTFS TRIM setting.");
                    }
                    var numbers = Regex.Matches(fs, @"(?:=|:)\s*(\d+)\b");
                    if (numbers.Count != 1) return new(false, false, "", "Cannot reliably read this filesystem setting on this Windows language/version.");
                    return new(true, true, numbers[0].Groups[1].Value);
                case "Tcp": return await ReadProperty("Get-NetTCPSetting -SettingName Internet", op.Target is "InitialRtoMs" or "MinRtoMs" ? op.Target[..^2] : op.Target);
                case "Offload": return await ReadProperty("Get-NetOffloadGlobalSetting", op.Target);
                case "Memory": return await ReadProperty("Get-MMAgent", op.Target);
                case "Ipv4": return await ReadProperty("Get-NetIPv4Protocol", op.Target);
                case "Ipv6": return await ReadProperty("Get-NetIPv6Protocol", op.Target);
                case "AdapterOffload":
                    return await ReadProperty("Get-NetAdapterAdvancedProperty -Name " + Q(op.Target) + " -RegistryKeyword " + Q(op.Name), "RegistryValue", array: true);
                case "Mtu":
                    return await ReadProperty($"Get-NetIPInterface -InterfaceAlias {Q(op.Target)} -AddressFamily {Q(op.Name)}", "NlMtu");
                case "Hibernate":
                    using (var power = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power"))
                        return new(true, true, Convert.ToInt32(power?.GetValue("HibernateEnabled", 0)).ToString());
                case "Wallpaper":
                    using (var desktop = Registry.Users.OpenSubKey(op.Target + @"\Control Panel\Desktop")) return new(true, true, desktop?.GetValue("WallPaper", "")?.ToString() ?? "");
                case "Nv": return NvProfile.Read(uint.Parse(op.Target));
                case "PowerPlan": case "PowerPlanEntry": return await PowerPlans.Read(op);
                case "Binding": return await ReadProperty($"Get-NetAdapterBinding -Name {Q(op.Target)} -ComponentID {Q(op.Name)}", "Enabled");
                case "StartupFile": return new(true, File.Exists(op.Target), File.Exists(op.Target) ? Convert.ToBase64String(await File.ReadAllBytesAsync(op.Target)) : "");
                default: return new(false, false, "", "No provider for " + op.Kind);
            }
        }
        catch (Exception error)
        {
            var permission = error is UnauthorizedAccessException or System.Security.SecurityException || error.HResult == unchecked((int)0x80070005) || error.Message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("PermissionDenied", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("UnauthorizedAccess", StringComparison.OrdinalIgnoreCase);
            return new(false, false, "", error.Message, permission);
        }
    }
    public async Task Write(Operation operation)
    {
        var op = Normalize(operation);
        switch (op.Kind)
        {
            case "Registry": WriteRegistry(op); break;
            case "Service": await ProcessRunner.Tool("sc.exe", "config", op.Target, "start=", op.Value switch { "0" => "boot", "1" => "system", "2" => "auto", "3" => "demand", "4" => "disabled", _ => throw new ArgumentException("Unsupported service startup mode.") }); break;
            case "Task":
                ScheduledTasks.Write(op.Target, op.Value == "true"); break;
            case "Bcd":
                if (op.Delete)
                {
                    if ((await Read(op)).Exists) await ProcessRunner.Tool("bcdedit.exe", "/deletevalue", "{current}", op.Target);
                }
                else await ProcessRunner.Tool("bcdedit.exe", "/set", "{current}", op.Target, op.Value);
                break;
            case "Power":
                var scheme = op.Type.Contains('|') ? op.Type.Split('|')[1] : "SCHEME_CURRENT";
                await ProcessRunner.Tool("powercfg.exe", op.Type.StartsWith("DC") ? "/setdcvalueindex" : "/setacvalueindex", scheme, op.Target, op.Name, op.Value);
                if ((await PowerPlans.Active()).Equals(scheme, StringComparison.OrdinalIgnoreCase)) await ProcessRunner.Tool("powercfg.exe", "/setactive", scheme);
                break;
            case "Fsutil":
                if (op.Target.Equals("DisableDeleteNotify", StringComparison.OrdinalIgnoreCase)) await ProcessRunner.Tool("fsutil.exe", "behavior", "set", op.Target, "NTFS", op.Value);
                else await ProcessRunner.Tool("fsutil.exe", "behavior", "set", op.Target, op.Value); break;
            case "Tcp": await SetProperty("Set-NetTCPSetting -SettingName Internet", op.Target, op.Value); break;
            case "Offload": await SetProperty("Set-NetOffloadGlobalSetting", op.Target, op.Value); break;
            case "Ipv4": await SetProperty("Set-NetIPv4Protocol", op.Target, op.Value); break;
            case "Ipv6": await SetProperty("Set-NetIPv6Protocol", op.Target, op.Value); break;
            case "Memory": await ProcessRunner.PowerShell($"{(op.Value == "true" ? "Enable" : "Disable")}-MMAgent -{Property(op.Target)}"); break;
            case "AdapterOffload": await ProcessRunner.PowerShell($"Set-NetAdapterAdvancedProperty -Name {Q(op.Target)} -RegistryKeyword {Q(op.Name)} -RegistryValue {Q(op.Value)} -NoRestart"); break;
            case "Mtu": await ProcessRunner.PowerShell($"Set-NetIPInterface -InterfaceAlias {Q(op.Target)} -AddressFamily {Q(op.Name)} -NlMtuBytes {int.Parse(op.Value)}"); break;
            case "Hibernate": await ProcessRunner.Tool("powercfg.exe", "/hibernate", op.Value == "0" ? "off" : "on"); break;
            case "Wallpaper":
                // A different-user UAC token must not change the wrong desktop.
                if (System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value != op.Target) throw new InvalidOperationException("Wallpaper requires the interactive user's account.");
                if (!NativeDesktop.SystemParametersInfo(20, 0, op.Value, 3)) throw new System.ComponentModel.Win32Exception(); break;
            case "Nv": NvProfile.Write(uint.Parse(op.Target), op.Delete ? 0 : uint.Parse(op.Value), op.Delete); break;
            case "PowerPlan": case "PowerPlanEntry": await PowerPlans.Write(op); break;
            case "Binding": await ProcessRunner.PowerShell($"{(op.Value == "true" ? "Enable" : "Disable")}-NetAdapterBinding -Name {Q(op.Target)} -ComponentID {Q(op.Name)} | Out-Null"); break;
            case "StartupFile":
                if (op.Delete) File.Delete(op.Target); else await File.WriteAllBytesAsync(op.Target, Convert.FromBase64String(op.Value)); break;
            default: throw new InvalidOperationException("Unsupported operation: " + op.Kind);
        }
    }
    public async Task Restore(Operation operation, Reading reading)
    {
        var op = Normalize(operation);
        if (op.Kind == "Registry") { RestoreRegistry(op, reading); return; }
        if (op.Kind is "PowerPlan" or "PowerPlanEntry") { await PowerPlans.Restore(op, reading); return; }
        if (op.Kind == "Service")
        {
            var previous = JsonSerializer.Deserialize<ServiceSnapshot>(reading.Value)!;
            await Write(op with { Value = previous.Start.ToString(CultureInfo.InvariantCulture) });
            using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + op.Target, true) ?? throw new InvalidOperationException("The original service is no longer installed.");
            if (previous.DelayedAutoStart is null) service.DeleteValue("DelayedAutoStart", false); else SetValue(service, "DelayedAutoStart", previous.DelayedAutoStart);
            return;
        }
        await Write(op with { Delete = !reading.Exists, Value = reading.Value });
    }
    public bool Equivalent(Operation operation, Reading left, Reading right)
    {
        if (left.Exists != right.Exists || left.Supported != right.Supported) return false;
        if (!left.Exists) return true;
        var op = Normalize(operation);
        if (op.Kind == "Registry")
        {
            var a = JsonSerializer.Deserialize<RegSnapshot>(left.Value, Catalog.Json)!;
            var b = JsonSerializer.Deserialize<RegSnapshot>(right.Value, Catalog.Json)!;
            return op.Tree ? JsonSerializer.Serialize(a.Tree, Catalog.Json) == JsonSerializer.Serialize(b.Tree, Catalog.Json) : RegEquals(a.Value, b.Value);
        }
        if (op.Kind is "PowerPlan" or "PowerPlanEntry")
        {
            var a = JsonSerializer.Deserialize<PowerSnapshot>(left.Value)!; var b = JsonSerializer.Deserialize<PowerSnapshot>(right.Value)!;
            return a.Active == b.Active && a.Exists == b.Exists && (!a.Exists || a.Export == b.Export);
        }
        return string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase);
    }
    public static bool Desired(Operation operation, Reading reading)
    {
        var op = Normalize(operation);
        if (!reading.Supported) return false;
        if (op.Kind == "Service") return reading.Exists && JsonSerializer.Deserialize<ServiceSnapshot>(reading.Value)!.Start.ToString(CultureInfo.InvariantCulture) == op.Value;
        if (op.Kind == "PowerPlan") return reading.Exists && JsonSerializer.Deserialize<PowerSnapshot>(reading.Value)!.Active.Equals(op.Target, StringComparison.OrdinalIgnoreCase);
        if (op.Kind == "Registry") return reading.Supported && (op.Delete ? !reading.Exists : reading.Exists && RegistryMatches(op, reading.Value));
        if (op.Delete) return !reading.Exists;
        return reading.Exists && string.Equals(op.Value, reading.Value, StringComparison.OrdinalIgnoreCase);
    }
    public static Operation Normalize(Operation op)
    {
        if (op.Kind == "Tcp" && op.Target is "MinRto" or "InitialRto") return op with { Target = op.Target + "Ms" };
        if (op.Kind != "Netsh") return op;
        var name = op.Name.ToLowerInvariant();
        if (op.Target == "tcp global") return name switch
        {
            "maxsynretransmissions" => op with { Kind = "Tcp", Target = "MaxSynRetransmissions" },
            "timestamps" => op with { Kind = "Tcp", Target = "Timestamps" },
            "ecncapability" => op with { Kind = "Tcp", Target = "EcnCapability" },
            "rsc" => op with { Kind = "Offload", Target = "ReceiveSegmentCoalescing" },
            "chimney" => op with { Kind = "Offload", Target = "Chimney" },
            "dca" => op with { Kind = "Offload", Target = "DirectCacheAccess" },
            "netdma" => op with { Kind = "Offload", Target = "NetDMA" },
            _ => op with { Kind = "Unsupported" }
        };
        if (op.Target == "ip global") return name switch
        {
            "taskoffload" => op with { Kind = "Offload", Target = "TaskOffload" },
            "icmpredirects" => op with { Kind = "Ipv4", Target = "IcmpRedirects" },
            "mldlevel" => op with { Kind = "Ipv6", Target = "MldLevel", Value = op.Value == "none" ? "None" : "All" },
            "dhcpmediasense" => op with { Kind = "Ipv4", Target = "DhcpMediaSense" },
            "mediasenseeventlog" => op with { Kind = "Ipv4", Target = "MediaSenseEventLog" },
            _ => op with { Kind = "Unsupported" }
        };
        if (op.Target == "tcp security" && name == "mpp") return op with { Kind = "Tcp", Target = "MemoryPressureProtection" };
        return op with { Kind = "Unsupported" };
    }
    private static async Task<Reading> ReadProperty(string command, string property, bool array = false)
    {
        var p = Property(property);
        var output = await ProcessRunner.PowerShell($"$o={command};if($null -eq $o -or $null -eq $o.PSObject.Properties[{Q(p)}]){{throw 'Setting is not exposed by this Windows version / driver.'}};[string]$o.{p}" + (array ? "[0]" : ""));
        if (string.IsNullOrWhiteSpace(output)) return new(false, false, "", "No readable current value.");
        return new(true, true, output);
    }
    private static Task<string> SetProperty(string command, string property, string value) => ProcessRunner.PowerShell(command + " -" + Property(property) + " " + Q(value) + " | Out-Null");
    private static string Property(string property) => Regex.IsMatch(property, "^[A-Za-z][A-Za-z0-9]+$") ? property : throw new ArgumentException("Invalid property name.");
    private static string Q(string value) => ProcessRunner.Quote(value);
    private static (RegistryKey Hive, string Path) Split(string target)
    {
        var index = target.IndexOf('\\'); if (index < 0) throw new ArgumentException("Invalid registry path.");
        var root = target[..index].ToUpperInvariant(); var path = target[(index + 1)..];
        var hive = root switch { "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine, "HKU" or "HKEY_USERS" => Registry.Users, "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser, "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot, _ => throw new ArgumentException("Unsupported registry hive.") };
        if (path.Length < 3) throw new ArgumentException("Registry root changes are not permitted.");
        return (hive, path);
    }
    private static Reading ReadRegistry(Operation op)
    {
        var (hive, path) = Split(op.Target);
        if (hive == Registry.LocalMachine && path.StartsWith(@"SYSTEM\CurrentControlSet\Services\", StringComparison.OrdinalIgnoreCase))
        {
            var serviceName = path.Split('\\')[3];
            using var service = hive.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + serviceName);
            if (service is null) return new(false, false, "", "Service / driver is not installed.");
        }
        using var key = hive.OpenSubKey(path);
        var snapshot = new RegSnapshot { KeyExisted = key is not null };
        if (key is null)
        {
            var partial = path;
            while (partial.Contains('\\'))
            {
                using var existing = hive.OpenSubKey(partial);
                if (existing is not null) break;
                snapshot.MissingParents.Add(partial); partial = partial[..partial.LastIndexOf('\\')];
            }
        }
        if (op.Tree) snapshot.Tree = key is null ? null : ReadTree(key);
        else if (key?.GetValueNames().Contains(op.Name, StringComparer.OrdinalIgnoreCase) == true) snapshot.Value = ReadValue(key, op.Name);
        return new(true, op.Tree ? key is not null : snapshot.Value is not null, JsonSerializer.Serialize(snapshot, Catalog.Json));
    }
    private static RegValue ReadValue(RegistryKey key, string name)
    {
        var kind = key.GetValueKind(name); var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)!;
        return new(kind.ToString(), kind switch
        {
            RegistryValueKind.DWord => [unchecked((uint)(int)value).ToString(CultureInfo.InvariantCulture)],
            RegistryValueKind.QWord => [unchecked((ulong)(long)value).ToString(CultureInfo.InvariantCulture)],
            RegistryValueKind.Binary or RegistryValueKind.None => [Convert.ToBase64String((byte[])value)],
            RegistryValueKind.MultiString => (string[])value,
            _ => [value.ToString() ?? ""]
        });
    }
    private static RegTree ReadTree(RegistryKey key)
    {
        var tree = new RegTree();
        tree.Security = key.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group);
        foreach (var name in key.GetValueNames().Order(StringComparer.OrdinalIgnoreCase)) tree.Values[name] = ReadValue(key, name);
        foreach (var name in key.GetSubKeyNames().Order(StringComparer.OrdinalIgnoreCase)) { using var child = key.OpenSubKey(name)!; tree.Children[name] = ReadTree(child); }
        return tree;
    }
    private static void WriteRegistry(Operation op)
    {
        var (hive, path) = Split(op.Target);
        if (op.Delete && op.Tree) { hive.DeleteSubKeyTree(path, false); return; }
        if (op.Delete) { using var key = hive.OpenSubKey(path, true); key?.DeleteValue(op.Name, false); return; }
        using var destination = hive.CreateSubKey(path, true);
        SetValue(destination, op.Name, Expected(op));
    }
    private static void RestoreRegistry(Operation op, Reading reading)
    {
        var (hive, path) = Split(op.Target); var old = JsonSerializer.Deserialize<RegSnapshot>(reading.Value, Catalog.Json)!;
        if (op.Tree)
        {
            hive.DeleteSubKeyTree(path, false);
            if (old.Tree is not null) { using var restored = hive.CreateSubKey(path, true); WriteTree(restored, old.Tree); }
        }
        else
        {
            if (old.Value is null) { using var key = hive.OpenSubKey(path, true); key?.DeleteValue(op.Name, false); }
            else { using var key = hive.CreateSubKey(path, true); SetValue(key, op.Name, old.Value); }
        }
        foreach (var missing in old.MissingParents)
        {
            using var key = hive.OpenSubKey(missing);
            if (key is null || key.SubKeyCount > 0 || key.ValueCount > 0) continue;
            key.Close(); hive.DeleteSubKey(missing, false);
        }
    }
    private static void WriteTree(RegistryKey key, RegTree tree)
    {
        foreach (var (name, value) in tree.Values) SetValue(key, name, value);
        foreach (var (name, value) in tree.Children) { using var child = key.CreateSubKey(name, true); WriteTree(child, value); }
        if (tree.Security.Length > 0) { var security = new RegistrySecurity(); security.SetSecurityDescriptorSddlForm(tree.Security); key.SetAccessControl(security); }
    }
    private static void SetValue(RegistryKey key, string name, RegValue value)
    {
        var kind = Enum.Parse<RegistryValueKind>(value.Kind);
        object data = kind switch { RegistryValueKind.DWord => unchecked((int)uint.Parse(value.Data[0])), RegistryValueKind.QWord => unchecked((long)ulong.Parse(value.Data[0])), RegistryValueKind.Binary or RegistryValueKind.None => Convert.FromBase64String(value.Data[0]), RegistryValueKind.MultiString => value.Data, _ => value.Data[0] };
        key.SetValue(name, data, kind);
    }
    public static RegValue Expected(Operation op)
    {
        var type = op.Type.ToUpperInvariant();
        return type switch
        {
            "REG_DWORD" => new("DWord", [Unsigned(op.Value).ToString()]),
            "REG_QWORD" => new("QWord", [Unsigned(op.Value).ToString()]),
            "REG_BINARY" => new("Binary", [Convert.ToBase64String(Convert.FromHexString(op.Value.Replace(" ", "")))]),
            "REG_MULTI_SZ" => new("MultiString", op.Value.Split("\\0", StringSplitOptions.RemoveEmptyEntries)),
            "REG_EXPAND_SZ" => new("ExpandString", [op.Value]),
            _ => new("String", [op.Value])
        };
    }
    private static ulong Unsigned(string value) => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? ulong.Parse(value[2..], NumberStyles.HexNumber) : ulong.Parse(value, CultureInfo.InvariantCulture);
    private static bool RegEquals(RegValue? a, RegValue? b) => a is null || b is null ? a == b : a.Kind == b.Kind && a.Data.SequenceEqual(b.Data);
    public static bool RegistryMatches(Operation operation, string snapshot) => RegEquals(JsonSerializer.Deserialize<RegSnapshot>(snapshot, Catalog.Json)!.Value, Expected(operation));
}

internal static class NativeDesktop
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);
}
