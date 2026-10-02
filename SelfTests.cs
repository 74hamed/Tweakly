using System.Text.Json;
using System.IO.Pipes;

namespace Tweakly;

public sealed record TestReport(int Passed, int Failed, List<string> Results);
public static class SelfTests
{
    private sealed class Fake : ISystemBackend
    {
        public Dictionary<string, Reading> Values = [];
        public List<string> Writes = [];
        public HashSet<string> Unsupported = [];
        public bool FailVerification, ThrowAfterWrite;
        public Task<Reading> Read(Operation op) => Task.FromResult(Unsupported.Contains(op.Key) ? new Reading(false, false, "", "Not installed") : Values.GetValueOrDefault(op.Key, Empty(op)));
        public Task Write(Operation op)
        {
            Writes.Add(op.Key);
            Values[op.Key] = op.Delete ? Empty(op) : Value(op, FailVerification ? "98" : op.Value);
            if (ThrowAfterWrite) throw new IOException("Simulated interruption after a write");
            return Task.CompletedTask;
        }
        public Task Restore(Operation op, Reading reading) { Values[op.Key] = reading; Writes.Add("undo:" + op.Key); return Task.CompletedTask; }
        public bool Equivalent(Operation op, Reading a, Reading b) => new WindowsBackend().Equivalent(op, a, b);
    }
    private static Reading Empty(Operation op) => new(true, false, op.Kind == "Registry" ? JsonSerializer.Serialize(new RegSnapshot()) : "");
    private static Reading Value(Operation op, string value) => new(true, true, op.Kind == "Registry" ? JsonSerializer.Serialize(new RegSnapshot { KeyExisted = true, Value = WindowsBackend.Expected(op with { Value = value }) }) : value);
    private static readonly Operation A = FeatureBuilder.Registry(@"HKCU\Software\TweaklyTest", "A", "1");
    private static readonly Operation B = FeatureBuilder.Registry(@"HKCU\Software\TweaklyTest", "B", "2");
    private static readonly Tweak T = new() { Id = "test", Category = "test", Operations = [A, B] };
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static async Task<TestReport> Run()
    {
        var results = new List<string>(); var passed = 0; var failed = 0;
        async Task Test(string name, Func<Task> action) { try { await action(); passed++; results.Add("PASS " + name); } catch (Exception e) { failed++; results.Add("FAIL " + name + ": " + e.Message); } }
        await Test("All 90 options have unique IDs, bilingual effects and actual handlers/settings", () =>
        {
            Assert(Catalog.All.Count == 90, "Unexpected option count"); Assert(Catalog.All.Select(t => t.Id).Distinct().Count() == Catalog.All.Count, "Duplicate option IDs");
            foreach (var tweak in Catalog.All) { Assert(tweak.TitleFa.Length > 0 && tweak.TitleEn.Length > 0 && tweak.DescriptionEn.Length > 30 && tweak.DescriptionFa.Length > 30, tweak.Id + " missing translation/effect"); Assert(!tweak.DescriptionEn.StartsWith("Apply this option only"), "Generic effect: " + tweak.Id); Assert(tweak.Handler.Length > 0 || tweak.Operations.Count > 0, tweak.Id + " has no implementation"); }
            return Task.CompletedTask;
        });
        await Test("Registry targets and data are valid, without source shell variables", () =>
        {
            using var templatesStream = typeof(Catalog).Assembly.GetManifestResourceStream("Tweakly.Data.ram.json")!;
            var templates = JsonSerializer.Deserialize<Dictionary<string, List<Operation>>>(templatesStream, Catalog.Json)!;
            Assert(templates.Keys.Order().SequenceEqual(new[] { "4", "8", "12", "16", "24", "32", "48", "64", "128" }.Order()), "Incomplete RAM presets");
            foreach (var op in Catalog.All.SelectMany(t => t.Operations).Concat(templates.Values.SelectMany(v => v)))
            {
                Assert(!op.Target.Contains('%') && !op.Target.Contains('!'), "Unresolved source variable: " + op.Target);
                if (op.Kind == "Registry" && !op.Delete)
                {
                    var expected = WindowsBackend.Expected(op); if (expected.Kind == "DWord") Assert(uint.TryParse(expected.Data[0], out _), "DWORD overflow: " + op);
                }
            }
            return Task.CompletedTask;
        });
        await Test("Only explicitly selected operations run", async () =>
        {
            var fake = new Fake(); var session = await new Engine(fake, _ => { }).Apply(T, [A], "test"); Assert(session.Status == "Completed", session.Error); Assert(fake.Writes.SequenceEqual([A.Key]), "Another option was written");
        });
        await Test("All original values are saved before the first write", async () =>
        {
            var fake = new Fake(); var backedUp = false;
            var session = await new Engine(fake, s => { if (fake.Writes.Count == 0 && s.Changes.Count == 2) backedUp = true; if (fake.Writes.Count > 0) Assert(backedUp, "Write before complete backup"); }).Apply(T, [A, B], "test"); Assert(session.Status == "Completed", session.Error);
        });
        await Test("Backup write failure prevents every system change", async () =>
        {
            var fake = new Fake(); try { await new Engine(fake, _ => throw new IOException("Disk full")).Apply(T, [A], "test"); } catch (IOException) { }
            Assert(fake.Writes.Count == 0, "Mutation occurred after backup failure");
        });
        await Test("Existing non-default settings are restored exactly", async () =>
        {
            var fake = new Fake(); var original = Value(A, "777"); fake.Values[A.Key] = original;
            var engine = new Engine(fake, _ => { }); var session = await engine.Apply(T, [A, B], "test"); await engine.Undo(session);
            Assert(session.Status == "Restored", session.Error); Assert(fake.Values[A.Key].Value == original.Value, "Original value lost"); Assert(!fake.Values[B.Key].Exists, "Originally absent value not removed");
        });
        await Test("Repeat apply is a no-op and does not overwrite the original backup", async () =>
        {
            var fake = new Fake(); var engine = new Engine(fake, _ => { }); var original = await engine.Apply(T, [A], "test"); var second = await engine.Apply(T, [A], "test");
            Assert(fake.Writes.Count == 1 && second.Changes[0].State == "Unchanged", "Repeated write"); await engine.Undo(original); Assert(!fake.Values[A.Key].Exists, "Original absence lost");
        });
        await Test("Missing services / unsupported targets are skipped explicitly", async () =>
        {
            var fake = new Fake(); fake.Unsupported.Add(B.Key); var session = await new Engine(fake, _ => { }).Apply(T, [A, B], "test");
            Assert(session.Status == "CompletedWithSkips" && session.Messages.Count == 1, "Missing target not reported"); Assert(!fake.Writes.Contains(B.Key), "Unsupported target written");
        });
        await Test("No compatible targets means failure with zero changes", async () =>
        {
            var fake = new Fake(); fake.Unsupported.Add(A.Key); var session = await new Engine(fake, _ => { }).Apply(T, [A], "test"); Assert(session.Status == "Failed" && fake.Writes.Count == 0, "Empty action reported success");
        });
        await Test("Verification failure stops before the next operation", async () =>
        {
            var fake = new Fake { FailVerification = true }; var session = await new Engine(fake, _ => { }).Apply(T, [A, B], "test"); Assert(session.Status == "Failed" && fake.Writes.Count == 1, "Continued after failed verification");
        });
        await Test("Interrupted write keeps a recoverable intent journal", async () =>
        {
            var fake = new Fake { ThrowAfterWrite = true }; var engine = new Engine(fake, _ => { }); var session = await engine.Apply(T, [A, B], "test"); Assert(session.Changes[0].State == "Writing", "Write intent lost");
            fake.ThrowAfterWrite = false; await engine.Undo(session); Assert(session.Status == "Restored" && !fake.Values[A.Key].Exists, "Interrupted change could not be restored");
        });
        await Test("Undo preserves a newer external change", async () =>
        {
            var fake = new Fake(); var engine = new Engine(fake, _ => { }); var session = await engine.Apply(T, [A], "test"); fake.Values[A.Key] = Value(A, "123"); await engine.Undo(session);
            Assert(session.Status == "UndoFailed" && WindowsBackend.RegistryMatches(A with { Value = "123" }, fake.Values[A.Key].Value), "External setting overwritten");
        });
        await Test("Interrupted Undo refuses an unexpected newer value", async () =>
        {
            var fake = new Fake { ThrowAfterWrite = true }; var engine = new Engine(fake, _ => { }); var session = await engine.Apply(T, [A], "test");
            fake.Values[A.Key] = Value(A, "123"); fake.ThrowAfterWrite = false; await engine.Undo(session);
            Assert(session.Status == "UndoFailed" && WindowsBackend.RegistryMatches(A with { Value = "123" }, fake.Values[A.Key].Value), "Unexpected interrupted state overwritten");
        });
        await Test("Undo refuses a setting whose current value cannot be read", async () =>
        {
            var fake = new Fake(); var engine = new Engine(fake, _ => { }); var session = await engine.Apply(T, [A], "test"); fake.Unsupported.Add(A.Key); await engine.Undo(session);
            Assert(session.Status == "UndoFailed" && fake.Writes.Count == 1, "Blind restoration of unreadable setting");
        });
        await Test("Service snapshots retain delayed-start state independently of the target startup mode", () =>
        {
            var op = new Operation { Kind = "Service", Target = "test", Value = "2" }; var backend = new WindowsBackend();
            var delayed = new Reading(true, true, JsonSerializer.Serialize(new ServiceSnapshot(2, new("DWord", ["1"]))));
            var ordinary = new Reading(true, true, JsonSerializer.Serialize(new ServiceSnapshot(2, null)));
            Assert(WindowsBackend.Desired(op, delayed), "Delayed automatic startup was not recognized");
            Assert(!backend.Equivalent(op, delayed, ordinary), "Delayed-start backup state was discarded");
            return Task.CompletedTask;
        });
        await Test("Undo restores in reverse dependency order", async () =>
        {
            var fake = new Fake(); var engine = new Engine(fake, _ => { }); var session = await engine.Apply(T, [A, B], "test"); await engine.Undo(session); Assert(fake.Writes.TakeLast(2).SequenceEqual(["undo:" + B.Key, "undo:" + A.Key]), "Undo order changed");
        });
        await Test("User-controlled PowerShell values are literal-quoted", () =>
        {
            Assert(ProcessRunner.Quote("a';Remove-Item x;'") == "'a'';Remove-Item x;'''", "Unsafe quoting");
            try { FeatureBuilder.Input([], "adapter"); throw new Exception("Missing input accepted"); } catch (ArgumentException) { }
            return Task.CompletedTask;
        });
        await Test("Legacy TCP switches have explicit unsupported states", () =>
        {
            Assert(WindowsBackend.Normalize(new() { Kind = "Netsh", Target = "tcp security", Name = "profiles" }).Kind == "Unsupported", "Legacy security switch was blindly executed"); return Task.CompletedTask;
        });
        await Test("The local pipe permits same-account IPC with a protected explicit SID ACL", async () =>
        {
            var name = "Tweakly-" + Guid.NewGuid().ToString("N"); using var server = Worker.CreatePipe(name);
            using var client = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var wait = server.WaitForConnectionAsync(timeout.Token); await client.ConnectAsync(timeout.Token); await wait;
            var received = new byte[1]; var read = server.ReadExactlyAsync(received, timeout.Token).AsTask(); await client.WriteAsync(new byte[] { 42 }, timeout.Token); await read;
            Assert(received[0] == 42, "IPC roundtrip failed");
            var acl = server.GetAccessControl(); Assert(acl.AreAccessRulesProtected, "IPC ACL inherits unwanted identities");
        });
        return new(passed, failed, results);
    }
}
