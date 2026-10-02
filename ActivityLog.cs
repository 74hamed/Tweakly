using System.Text.Json;

namespace Tweakly;

// UI-only results (including UAC cancellation) never become elevated Undo input.
public static class ActivityLog
{
    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tweakly", "Activity");
    public static void Save(Tweak tweak, Result result, string sid)
    {
        if (result.Session is not null) return;
        var session = new Session { TweakId = tweak.Id, Category = tweak.Category, UserSid = sid, Status = result.Status, Undoable = false, Reboot = result.Reboot, Error = result.Success ? "" : result.Message };
        if (result.Success) session.Messages.Add(result.Message);
        Directory.CreateDirectory(Root);
        File.WriteAllText(Path.Combine(Root, session.Id + ".json"), JsonSerializer.Serialize(session, Catalog.Json));
    }
    public static List<Session> Read(string sid)
    {
        var entries = new List<Session>();
        try
        {
            if (!Directory.Exists(Root)) return entries;
            foreach (var path in Directory.EnumerateFiles(Root, "*.json"))
            {
                try { var entry = JsonSerializer.Deserialize<Session>(File.ReadAllText(path), Catalog.Json); if (entry?.UserSid == sid) { entry.Undoable = false; entries.Add(entry); } } catch { }
            }
        }
        catch (UnauthorizedAccessException) { }
        return entries;
    }
}
