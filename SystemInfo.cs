using Microsoft.Win32;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Tweakly;

public sealed record Gpu(string Vendor, string Name, string RegistryPath);
public sealed record Adapter(string Id, string Name, string Description, bool Ethernet, string RegistryPath)
{
    public override string ToString() => Name + " · " + Description;
}
public sealed class SystemInfo
{
    public string Sid { get; } = WindowsIdentity.GetCurrent().User!.Value;
    public string Cpu { get; }
    public string CpuVendor { get; }
    public int Build { get; }
    public string Os => Build >= 22000 ? "Windows 11" : "Windows 10";
    public double RamGb { get; }
    public List<Gpu> Gpus { get; } = [];
    public List<Adapter> Adapters { get; } = [];
    public SystemInfo()
    {
        using var cpu = Open(Registry.LocalMachine, @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        Cpu = (cpu?.GetValue("ProcessorNameString")?.ToString() ?? "Unknown CPU").Trim();
        CpuVendor = cpu?.GetValue("VendorIdentifier")?.ToString() switch { "AuthenticAMD" => "AMD", "GenuineIntel" => "Intel", _ => "Unknown" };
        using var os = Open(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        Build = int.TryParse(os?.GetValue("CurrentBuildNumber")?.ToString(), out var build) ? build : Environment.OSVersion.Version.Build;
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref memory)) RamGb = memory.TotalPhysical / 1073741824d;
        const string gpuRoot = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        using var gpuClass = Open(Registry.LocalMachine, gpuRoot);
        foreach (var child in gpuClass?.GetSubKeyNames() ?? [])
        {
            if (!int.TryParse(child, out _)) continue;
            using var key = Open(gpuClass!, child);
            var id = key?.GetValue("MatchingDeviceId")?.ToString() ?? "";
            var name = key?.GetValue("DriverDesc")?.ToString() ?? "";
            var vendor = id.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) ? "NVIDIA" : id.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) ? "AMD" : id.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase) ? "Intel" : "";
            if (vendor.Length > 0) Gpus.Add(new(vendor, name, "HKLM\\" + gpuRoot + "\\" + child));
        }
        const string nicRoot = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
        using var nicClass = Open(Registry.LocalMachine, nicRoot);
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)) continue;
            var path = "";
            foreach (var child in nicClass?.GetSubKeyNames() ?? [])
            {
                if (!int.TryParse(child, out _)) continue;
                using var key = Open(nicClass!, child);
                if (string.Equals(key?.GetValue("NetCfgInstanceId")?.ToString()?.Trim('{', '}'), nic.Id.Trim('{', '}'), StringComparison.OrdinalIgnoreCase)) { path = "HKLM\\" + nicRoot + "\\" + child; break; }
            }
            Adapters.Add(new(nic.Id, nic.Name, nic.Description, nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet, path));
        }
    }
    private static RegistryKey? Open(RegistryKey root, string path)
    {
        try { return root.OpenSubKey(path); }
        catch (System.Security.SecurityException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
    public string? Incompatible(Tweak tweak)
    {
        if (!Environment.Is64BitOperatingSystem || Build < 19045) return "Windows 10 22H2 / Windows 11 x64 is required.";
        if (tweak.Os.Length > 0 && tweak.Os != (Build >= 22000 ? "11" : "10")) return "This option targets Windows " + tweak.Os + ".";
        if (tweak.Vendor.Length > 0 && (tweak.Category == "cpu" ? tweak.Vendor != CpuVendor : !Gpus.Any(g => g.Vendor == tweak.Vendor))) return tweak.Vendor + " hardware was not detected.";
        if (tweak.EthernetOnly && !Adapters.Any(a => a.Ethernet)) return "No Ethernet adapter was detected.";
        return null;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, AvailableExtended; }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
}
