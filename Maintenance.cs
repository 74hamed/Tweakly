using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;

namespace Tweakly;

public static class Maintenance
{
    public static async Task CreateRestorePoint()
    {
        var name = "Tweakly " + Guid.NewGuid().ToString("N")[..10];
        await ProcessRunner.PowerShell($"Enable-ComputerRestore -Drive ($env:SystemDrive + '\\');Checkpoint-Computer -Description {ProcessRunner.Quote(name)} -RestorePointType MODIFY_SETTINGS; if(-not (Get-ComputerRestorePoint | Where-Object Description -eq {ProcessRunner.Quote(name)})){{throw 'Windows did not create the restore point (protection unavailable or frequency limit). No requested tweak has been executed.'}}", 120);
    }
    public static async Task<Result> Run(Tweak tweak, Dictionary<string, string> inputs, SystemInfo info, Journal journal)
    {
        var session = new Session { TweakId = tweak.Id, Category = tweak.Category, UserSid = info.Sid, Reboot = tweak.Reboot, Undoable = false };
        journal.Save(session);
        try
        {
            if (tweak.Destructive) { await CreateRestorePoint(); session.Messages.Add("Verified restore point created before maintenance."); }
            session.Status = "Running"; journal.Save(session);
            switch (tweak.Handler)
            {
                case "RestorePoint": await CreateRestorePoint(); session.Messages.Add("Restore point verified in Windows."); break;
                case "OptimizeDrive":
                    var drive = FeatureBuilder.Input(inputs, "drive");
                    if (drive.Length != 1 || !DriveInfo.GetDrives().Any(d => d.IsReady && d.Name.StartsWith(drive + ":", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Select a mounted drive.");
                    session.Messages.Add(await ProcessRunner.Run(Path.Combine(Environment.SystemDirectory, "defrag.exe"), [drive + ":", "/O", "/U"], 3600)); break;
                case "RemoveApps":
                case "RemoveCopilot":
                    var patterns = tweak.Handler == "RemoveCopilot" ? new[] { "Microsoft.549981C3F5F10", "Microsoft.Windows.Ai.Copilot.Provider", "Microsoft.Copilot" } : FeatureBuilder.Input(inputs, "apps").Split(',', StringSplitOptions.RemoveEmptyEntries);
                    if (tweak.Handler == "RemoveApps" && (patterns.Length == 0 || patterns.Any(p => !FeatureBuilder.InboxApps.ContainsKey(p)))) throw new ArgumentException("Select one or more listed inbox apps.");
                    var names = "@(" + string.Join(',', patterns.Select(ProcessRunner.Quote)) + ")";
                    var sid = ProcessRunner.Quote(info.Sid);
                    var inventory = await ProcessRunner.PowerShell($"@(Get-AppxPackage -User {sid} | Where-Object {{ $_.Name -in {names} }} | Select-Object Name,PackageFullName,InstallLocation) | ConvertTo-Json -Depth 3 -Compress");
                    session.Messages.Add("Package inventory (reinstall may need Windows Store / internet): " + inventory); journal.Save(session);
                    session.Messages.Add(await ProcessRunner.PowerShell($"$p=@(Get-AppxPackage -User {sid} | Where-Object {{$_.Name -in {names}}});if($p.Count -eq 0){{throw 'None of the selected packages is installed.'}};foreach($a in $p){{Remove-AppxPackage -Package $a.PackageFullName -User {sid}}};$remaining=@(Get-AppxPackage -User {sid} | Where-Object {{$_.Name -in {names}}});if($remaining.Count -gt 0){{throw 'Some selected packages could not be removed.'}}; 'Selected packages removed and absence verified.'", 180)); break;
                case "DeviceClean":
                    var ids = FeatureBuilder.Input(inputs, "devices").Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var disconnected = await FeatureBuilder.DeviceChoices("", false);
                    if (ids.Length == 0 || ids.Any(id => !disconnected.Any(d => d.Value == id))) throw new ArgumentException("Only currently disconnected devices from the displayed list may be removed.");
                    session.Messages.Add("Selected device inventory: " + JsonSerializer.Serialize(disconnected.Where(d => ids.Contains(d.Value)))); journal.Save(session);
                    foreach (var id in ids)
                    {
                        session.Messages.Add(await ProcessRunner.Tool("pnputil.exe", "/remove-device", id));
                        if ((await FeatureBuilder.DeviceChoices("", false)).Any(d => d.Value == id)) throw new InvalidOperationException("Device removal could not be verified: " + id);
                        journal.Save(session);
                    }
                    break;
                case "RegisterApps":
                    session.Messages.Add(await ProcessRunner.PowerShell("$errors=@();Get-AppxPackage | ForEach-Object { $manifest=Join-Path $_.InstallLocation 'AppxManifest.xml';if(Test-Path -LiteralPath $manifest){try{Add-AppxPackage -Register $manifest -DisableDevelopmentMode}catch{$errors+=$_.Exception.Message}}};if($errors.Count){throw ($errors -join [Environment]::NewLine)};'Existing manifests re-registered. Missing package payloads require Store reinstall.'", 180)); break;
                case "InstallEdge":
                    session.Messages.Add(await Winget(["install", "--id", "Microsoft.Edge", "--exact", "--accept-source-agreements", "--accept-package-agreements", "--silent"]));
                    if (!EdgeInstalled()) throw new InvalidOperationException("Edge installation could not be verified."); break;
                case "RemoveEdge":
                    session.Messages.Add("Supported uninstall requested. WebView2 and protected Windows components are preserved."); journal.Save(session);
                    session.Messages.Add(await Winget(["uninstall", "--id", "Microsoft.Edge", "--exact", "--silent"]));
                    if (EdgeInstalled()) throw new InvalidOperationException("Windows still reports Edge installed; this Windows edition / region may not support uninstall."); break;
                case "DefaultPower":
                    foreach (var plan in await PowerPlans.List()) session.Messages.Add("Power plan backup " + plan + ": " + await PowerPlans.Export(plan));
                    journal.Save(session);
                    await ProcessRunner.Tool("powercfg.exe", "/restoredefaultschemes");
                    if (!(await PowerPlans.List()).Contains("381b4222-f694-41f0-9685-ff5bb260df2e")) throw new InvalidOperationException("Default Balanced plan was not restored.");
                    session.Messages.Add("Default plans verified. Existing power plan exports are stored in this protected journal."); break;
                default: throw new InvalidOperationException("Unsupported maintenance action.");
            }
            session.Status = "Completed";
        }
        catch (Exception error) { session.Status = "Failed"; session.Error = error.Message; }
        journal.Save(session);
        return new(session.Status == "Completed", session.Status, session.Error.Length > 0 ? session.Error : string.Join(Environment.NewLine, session.Messages), tweak.Reboot, session);
    }
    public static async Task<Result> RunInteractive(Tweak tweak, Dictionary<string, string> inputs)
    {
        if (tweak.Handler == "Ping")
        {
            var region = FeatureBuilder.Input(inputs, "region"); if (!FeatureBuilder.Regions.Contains(region)) throw new ArgumentException("Select a region.");
            using var ping = new Ping(); var timings = new List<long>(); var failed = 0;
            for (var count = 0; count < 8; count++)
            {
                try { var response = await ping.SendPingAsync("ping-" + region + ".ds.on.epicgames.com", 2000); if (response.Status == IPStatus.Success) timings.Add(response.RoundtripTime); else failed++; }
                catch (PingException) { failed++; }
            }
            return new(timings.Count > 0, timings.Count > 0 ? "Completed" : "Unavailable", timings.Count > 0 ? $"{region.ToUpperInvariant()}: average {timings.Average():0.0} ms · minimum {timings.Min()} ms · maximum {timings.Max()} ms · loss {failed * 100 / 8}% (ICMP test; not in-game latency)" : "The server did not answer ICMP. Check connectivity; the endpoint may no longer accept ping.");
        }
        var target = tweak.Handler switch { "OpenApps" => "ms-settings:appsfeatures", "Bufferbloat" => "https://www.waveform.com/tools/bufferbloat", "DiskCleanup" => Path.Combine(Environment.SystemDirectory, "cleanmgr.exe"), "SystemRestore" => Path.Combine(Environment.SystemDirectory, "rstrui.exe"), _ => throw new InvalidOperationException("Unknown interactive action.") };
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        return new(true, "Opened", "Windows / browser tool opened. Its changes are controlled by its own interface.");
    }
    public static void EnsureWallpaper()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Tweakly", "wallpaper.png");
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Tweakly.Assets.wallpaper.png")!;
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read); stream.CopyTo(file);
    }
    private static async Task<string> Winget(string[] arguments)
    {
        var candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe");
        if (!File.Exists(candidate)) throw new InvalidOperationException("App Installer / winget is required for this online action.");
        return await ProcessRunner.Run(candidate, arguments, 600);
    }
    private static bool EdgeInstalled() => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe")) || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"));
}
