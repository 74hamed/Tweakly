using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace Tweakly;

public static class Worker
{
    private static readonly JsonSerializerOptions Wire = new() { PropertyNameCaseInsensitive = true };
    public static async Task<Result> Invoke(Request request, CancellationToken cancellation)
    {
        var pipeName = "Tweakly-" + Guid.NewGuid().ToString("N");
        using var pipe = CreatePipe(pipeName);
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        info.ArgumentList.Add("--worker"); info.ArgumentList.Add(pipeName);
        Process? process;
        try { process = Process.Start(info); }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223) { return new(false, "Cancelled", "Administrator permission was cancelled. Nothing was changed."); }
        if (process is null) return new(false, "Failed", "Could not start the administrator worker.");
        using (process)
        using (var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            connectionTimeout.CancelAfter(TimeSpan.FromSeconds(45));
            await pipe.WaitForConnectionAsync(connectionTimeout.Token);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var clientId) || clientId != process.Id) throw new InvalidOperationException("Unexpected worker connection.");
            var peer = new NTAccount(pipe.GetImpersonationUserName()).Translate(typeof(SecurityIdentifier));
            if (!peer.Equals(WindowsIdentity.GetCurrent().User)) throw new InvalidOperationException("Use your own account for UAC; another user's token is not supported.");
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, Wire));
            var response = await reader.ReadLineAsync(cancellation) ?? throw new IOException("Worker ended before sending a result. Check the protected history before retrying.");
            return JsonSerializer.Deserialize<Result>(response, Wire) ?? throw new IOException("Invalid worker response.");
        }
    }
    internal static NamedPipeServerStream CreatePipe(string pipeName)
    {
        var security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        // Explicit SID ACL permits the SAME account's elevated token. CurrentUserOnly
        // would reject the elevation-level difference on Windows.
        return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, security);
    }
    public static async Task<int> Run(string pipeName)
    {
        if (!pipeName.StartsWith("Tweakly-", StringComparison.Ordinal) || pipeName.Length != 40) return 2;
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return 3;
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
        await pipe.ConnectAsync(30000);
        using var reader = new StreamReader(pipe, leaveOpen: true); using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        var payload = await reader.ReadLineAsync() ?? throw new IOException("No operation received.");
        if (payload.Length > 32000) throw new InvalidOperationException("Request is too large.");
        var request = JsonSerializer.Deserialize<Request>(payload, Wire)!;
        Result result;
        using (var guard = new Mutex(false, "Global\\Tweakly.SystemChanges"))
        {
            var acquired = false;
            try
            {
                try { acquired = guard.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                result = acquired ? Execute(request).GetAwaiter().GetResult() : new(false, "Busy", "Another Tweakly operation is running. Wait for it to finish.");
            }
            catch (Exception error) { result = new(false, "Failed", error.Message); }
            finally { if (acquired) guard.ReleaseMutex(); }
        }
        await writer.WriteLineAsync(JsonSerializer.Serialize(result, Wire)); return result.Success ? 0 : 1;
    }
    private static async Task<Result> Execute(Request request)
    {
        Result result;
        try
        {
            var tweak = Catalog.Get(request.Id); var info = new SystemInfo();
            if (request.Mode == "apply" && info.Incompatible(tweak) is string unsupported) throw new InvalidOperationException(unsupported);
            var journal = new Journal(info.Sid); journal.Prepare();
            var backend = new WindowsBackend(); var engine = new Engine(backend, journal.Save);
            if (request.Mode == "undo")
            {
                var session = journal.Latest(tweak.Id) ?? throw new InvalidOperationException("No recorded changes are available for this option.");
                var undone = await engine.Undo(session);
                result = new(undone.Status == "Restored", undone.Status, undone.Error.Length > 0 ? undone.Error : "Your previous configuration was restored and verified.", undone.Reboot, undone);
            }
            else if (request.Mode is "undo-category" or "undo-all")
            {
                var sessions = journal.ReadAll().Where(s => (request.Mode == "undo-all" || s.Category == tweak.Category) && s.Undoable && s.Status != "Restored" && s.Changes.Any(c => c.State is "Writing" or "Applied")).ToList();
                if (sessions.Count == 0) throw new InvalidOperationException("No recorded changes in this category.");
                foreach (var session in sessions) { await engine.Undo(session); if (session.Status != "Restored") throw new InvalidOperationException(session.Error); }
                result = new(true, "Restored", "Recorded changes restored in reverse order.", sessions.Any(s => s.Reboot));
            }
            else if (request.Mode == "apply")
            {
                if (FeatureBuilder.Maintenance(tweak)) result = await Maintenance.Run(tweak, request.Inputs, info, journal);
                else
                {
                    var operations = await FeatureBuilder.Build(tweak, request.Inputs, info);
                    if (tweak.Destructive) await Maintenance.CreateRestorePoint();
                    if (tweak.Handler == "Wallpaper") Maintenance.EnsureWallpaper();
                    var session = await engine.Apply(tweak, operations, info.Sid);
                    var success = session.Status is "Completed" or "CompletedWithSkips";
                    result = new(success, session.Status, success ? $"{session.Changes.Count(c => c.State == "Applied")} changed · {session.Changes.Count(c => c.State == "Unchanged")} already set · {session.Messages.Count} unsupported\n" + string.Join('\n', session.Messages) : session.Error, tweak.Reboot && session.Changes.Any(c => c.State == "Applied"), session);
                }
            }
            else throw new ArgumentException("Unknown operation mode.");
        }
        catch (Exception error) { result = new(false, "Failed", error.Message); }
        return result;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint id);
}
