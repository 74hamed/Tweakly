using System.Diagnostics;
using System.Text;

namespace Tweakly;

public static class ProcessRunner
{
    public static async Task<string> Run(string executable, IEnumerable<string> arguments, int timeoutSeconds = 60)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start " + executable);
        var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw new TimeoutException(executable + " timed out. Check history before retrying."); }
        var stdout = await output; var stderr = await errors;
        if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(executable)} exited with {process.ExitCode}: {stderr} {stdout}".Trim());
        return stdout.Trim();
    }
    public static Task<string> PowerShell(string script, int timeoutSeconds = 60)
    {
        var command = "$ErrorActionPreference='Stop';$ProgressPreference='SilentlyContinue';try { " + script + " } catch { [Console]::Error.WriteLine($_.Exception.Message);exit 1 }";
        return Run(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command))], timeoutSeconds);
    }
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
    public static Task<string> Tool(string name, params string[] args) => Run(Path.Combine(Environment.SystemDirectory, name), args);
}
