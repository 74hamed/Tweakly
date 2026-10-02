using System.Text.Json;
using System.Diagnostics;
using System.Windows;

namespace Tweakly;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--worker") return Worker.Run(args[1]).GetAwaiter().GetResult();
            if (args.Length == 2 && args[0] == "--inspect") { Inspection.Run(args[1]).GetAwaiter().GetResult(); return 0; }
            if (args.Length >= 1 && args[0] == "--self-test")
            {
                var report = SelfTests.Run().GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(report, Catalog.Json));
                if (args.Length > 1) File.WriteAllText(args[1], JsonSerializer.Serialize(report, Catalog.Json));
                return report.Failed == 0 ? 0 : 1;
            }
            var app = new Application();
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Tweakly;component/Theme.xaml", UriKind.Relative) });
            app.DispatcherUnhandledException += (_, e) => { MessageBox.Show(e.Exception.Message, "Tweakly", MessageBoxButton.OK, MessageBoxImage.Error); e.Handled = true; };
            var preview = args.Length == 2 && args[0] is "--qa" or "--benchmark";
            var window = new MainWindow(preview);
            if (preview && args[0] == "--qa") window.Loaded += async (_, _) => { try { await window.CaptureQa(args[1]); app.Shutdown(); } catch (Exception e) { File.WriteAllText(Path.Combine(args[1], "qa-error.txt"), e.ToString()); app.Shutdown(1); } };
            if (preview && args[0] == "--benchmark") window.ContentRendered += (_, _) =>
            {
                var process = Process.GetCurrentProcess();
                File.WriteAllText(args[1], JsonSerializer.Serialize(new { StartupToFirstRenderMs = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds, WorkingSetMiB = process.WorkingSet64 / 1048576d, PrivateMiB = process.PrivateMemorySize64 / 1048576d, Elevated = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator), ReadOnly = true }, Catalog.Json));
                app.Shutdown();
            };
            return app.Run(window);
        }
        catch (Exception error)
        {
            if (args.Length > 0) Console.Error.WriteLine(error);
            else MessageBox.Show(error.Message, "Tweakly", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
}
