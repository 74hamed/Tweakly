using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Tweakly;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public SystemInfo System { get; } = new();
    private bool persian;
    public bool Persian { get => persian; set { persian = value; Changed(); } }
    public string Category { get; set; } = "overview";
    public string Query { get; set; } = "";
    public Tweak? Selected { get; set; }
    public Dictionary<string, string> Inputs { get; } = [];
    public List<Session> History => new Journal(System.Sid).ReadAll().Concat(ActivityLog.Read(System.Sid)).OrderByDescending(s => s.Started).ToList();
    public IEnumerable<Tweak> Visible => Catalog.All.Where(t => (Category == "all" || t.Category == Category) && (Query.Length == 0 || t.TitleEn.Contains(Query, StringComparison.OrdinalIgnoreCase) || t.TitleFa.Contains(Query, StringComparison.OrdinalIgnoreCase)));
    public string L(string en, string fa) => Persian ? fa : en;
    public MainViewModel()
    {
        persian = global::System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fa";
        try { var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tweakly", "preferences.json"); if (File.Exists(file)) persian = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(file))?.GetValueOrDefault("Persian", persian) ?? persian; } catch { }
    }
    public void SavePreferences()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tweakly"); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "preferences.json"), JsonSerializer.Serialize(new { Persian }));
    }
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}
