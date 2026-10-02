using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace Tweakly;

public sealed class Journal(string sid)
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Tweakly", "History");
    public void Prepare()
    {
        // Journals consumed by the elevated worker must not be writable by an unelevated user.
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var parent = new DirectoryInfo(Path.GetDirectoryName(Root)!);
        foreach (var folderToCheck in new[] { parent, new DirectoryInfo(Root) })
        {
            if (!folderToCheck.Exists) continue;
            if ((folderToCheck.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("The journal folder cannot be a link.");
            var owner = folderToCheck.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
            if (owner is null || (!owner.Equals(admins) && !owner.Equals(system))) throw new InvalidOperationException("An untrusted journal folder already exists. Move it aside before running Tweakly with administrator privileges.");
        }
        parent.Create();
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        acl.SetOwner(admins);
        foreach (var identity in new[] { new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        parent.SetAccessControl(acl);
        var folder = new DirectoryInfo(Root); folder.Create(); folder.SetAccessControl(acl);
    }
    public void Save(Session session)
    {
        if (session.UserSid != sid) throw new InvalidOperationException("Wrong journal owner.");
        var path = Path.Combine(Root, session.Id + ".json");
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { JsonSerializer.Serialize(stream, session, Catalog.Json); stream.Flush(true); }
        File.Move(temp, path, true);
    }
    public List<Session> ReadAll()
    {
        if (!Directory.Exists(Root)) return [];
        var sessions = new List<Session>();
        string[] files;
        try { files = Directory.GetFiles(Root, "*.json"); }
        catch (UnauthorizedAccessException) { return sessions; }
        foreach (var file in files)
        {
            try { var session = JsonSerializer.Deserialize<Session>(File.ReadAllText(file), Catalog.Json); if (session?.UserSid == sid) sessions.Add(session); } catch { }
        }
        return sessions.OrderByDescending(s => s.Started).ToList();
    }
    public Session? Latest(string id) => ReadAll().FirstOrDefault(s => s.TweakId == id && s.Undoable && s.Changes.Any(c => c.State is "Applied" or "Writing") && s.Status != "Restored");
}
