using System.Runtime.InteropServices;

namespace Tweakly;

public static class ScheduledTasks
{
    public static bool Read(string target) => Access(target, null);
    public static void Write(string target, bool enabled) => Access(target, enabled);
    private static bool Access(string target, bool? enabled)
    {
        object? service = null, folder = null, task = null;
        try
        {
            var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new NotSupportedException("Windows Task Scheduler is unavailable.");
            service = Activator.CreateInstance(type)!; ((dynamic)service).Connect();
            var full = "\\" + target.TrimStart('\\'); var split = full.LastIndexOf('\\');
            var path = split == 0 ? "\\" : full[..split]; var name = full[(split + 1)..];
            folder = ((dynamic)service).GetFolder(path); task = ((dynamic)folder).GetTask(name);
            if (enabled.HasValue) ((dynamic)task).Enabled = enabled.Value;
            return (bool)((dynamic)task).Enabled;
        }
        finally
        {
            foreach (var item in new[] { task, folder, service }) if (item is not null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item);
        }
    }
}
