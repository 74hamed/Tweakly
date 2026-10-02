namespace Tweakly;

// The only transaction boundary is one explicitly selected option.
public sealed class Engine(ISystemBackend backend, Action<Session> save)
{
    public async Task<Session> Apply(Tweak tweak, IEnumerable<Operation> operations, string sid)
    {
        var session = new Session { TweakId = tweak.Id, Category = tweak.Category, UserSid = sid, Reboot = tweak.Reboot };
        save(session); // A writable journal is a prerequisite, even before probing.
        try
        {
            foreach (var op in operations.DistinctBy(o => o.Key))
            {
                var before = await backend.Read(op);
                if (!before.Supported) { session.Messages.Add($"Skipped: {op.Target}/{op.Name} — {before.Reason}"); continue; }
                session.Changes.Add(new() { Operation = op, Before = before });
            }
            save(session); // Snapshot EVERY applicable target before the first write.
            if (session.Changes.Count == 0) throw new InvalidOperationException("No compatible settings were found; nothing was changed.");
            session.Status = "Running"; save(session);
            foreach (var change in session.Changes)
            {
                if (WindowsBackend.Desired(change.Operation, change.Before)) { change.State = "Unchanged"; save(session); continue; }
                // Capture write intent before mutation; an interrupted write remains recoverable.
                change.State = "Writing"; save(session);
                await backend.Write(change.Operation);
                change.After = await backend.Read(change.Operation);
                if (!change.After.Supported || !WindowsBackend.Desired(change.Operation, change.After))
                    throw new InvalidOperationException($"Verification failed: {change.Operation.Target}/{change.Operation.Name}");
                change.State = backend.Equivalent(change.Operation, change.Before, change.After) ? "Unchanged" : "Applied";
                save(session);
            }
            session.Status = session.Messages.Count > 0 ? "CompletedWithSkips" : "Completed";
        }
        catch (Exception error) { session.Status = "Failed"; session.Error = error.Message; }
        save(session); return session;
    }
    public async Task<Session> Undo(Session session)
    {
        if (!session.Undoable) throw new InvalidOperationException("This maintenance action has no exact Undo.");
        session.Status = "Undoing"; save(session);
        try
        {
            foreach (var change in session.Changes.AsEnumerable().Reverse())
            {
                if (change.State is "Prepared" or "Unchanged" or "Restored") continue;
                var now = await backend.Read(change.Operation);
                if (!now.Supported) throw new InvalidOperationException("Cannot read the current setting safely: " + now.Reason);
                if (change.After is not null && !backend.Equivalent(change.Operation, now, change.After) && !backend.Equivalent(change.Operation, now, change.Before))
                    throw new InvalidOperationException("This setting was changed outside Tweakly after this operation. Undo stopped to preserve the newer configuration.");
                if (change.After is null && !backend.Equivalent(change.Operation, now, change.Before) && !WindowsBackend.Desired(change.Operation, now))
                    throw new InvalidOperationException("An interrupted write has an unexpected current value. Undo stopped to preserve it; review the original journal.");
                await backend.Restore(change.Operation, change.Before);
                var restored = await backend.Read(change.Operation);
                if (!backend.Equivalent(change.Operation, restored, change.Before)) throw new InvalidOperationException("Undo verification failed: " + change.Operation.Target);
                change.State = "Restored"; save(session);
            }
            session.Status = "Restored"; session.Error = "";
        }
        catch (Exception error) { session.Status = "UndoFailed"; session.Error = error.Message; }
        save(session); return session;
    }
    public static bool Matches(Operation operation, Reading actual)
    {
        if (operation.Delete) return !actual.Exists;
        if (!actual.Exists) return false;
        if (operation.Kind == "Registry") return WindowsBackend.RegistryMatches(operation, actual.Value);
        return string.Equals(operation.Value, actual.Value, StringComparison.OrdinalIgnoreCase);
    }
}
