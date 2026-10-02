using System.Reflection;
using System.Text.Json;

namespace Tweakly;

public sealed record Operation
{
    public string Kind { get; init; } = "Registry";
    public string Target { get; init; } = "";
    public string Name { get; init; } = "";
    public string Value { get; init; } = "";
    public string Type { get; init; } = "REG_DWORD";
    public bool Delete { get; init; }
    public bool Tree { get; init; }
    public string Key => $"{Kind}|{Target}|{Name}|{Type}|{Tree}";
    public override string ToString() => $"{Kind} · {Target}{(Name.Length > 0 ? " → " + Name : "")} = {(Delete ? "[remove]" : Value)}";
}
public sealed record Tweak
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public string TitleEn { get; init; } = "";
    public string TitleFa { get; init; } = "";
    public string DescriptionEn { get; init; } = "";
    public string DescriptionFa { get; init; } = "";
    public string Risk { get; init; } = "Standard";
    public bool Reboot { get; init; }
    public string Vendor { get; init; } = "";
    public string Os { get; init; } = "";
    public string Handler { get; init; } = "";
    public string Input { get; init; } = "";
    public string Source { get; init; } = "";
    public bool EthernetOnly { get; init; }
    public List<Operation> Operations { get; init; } = [];
    public string Title(bool fa) => fa ? TitleFa : TitleEn;
    public string Description(bool fa) => fa ? DescriptionFa : DescriptionEn;
    public bool Destructive => Risk == "Destructive";
}
public static class Catalog
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static readonly IReadOnlyList<Tweak> All = Load();
    private static List<Tweak> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Tweakly.Data.catalog.json")!;
        return JsonSerializer.Deserialize<List<Tweak>>(stream, Json)!;
    }
    public static Tweak Get(string id) => All.SingleOrDefault(t => t.Id == id) ?? throw new ArgumentException("Unknown option.");
}
public sealed record Request(string Id, string Mode, Dictionary<string, string> Inputs);
public sealed record Reading(bool Supported, bool Exists, string Value, string Reason = "", bool RequiresElevation = false);
public sealed class Change
{
    public Operation Operation { get; set; } = new();
    public Reading Before { get; set; } = new(false, false, "");
    public Reading? After { get; set; }
    public string State { get; set; } = "Prepared";
}
public sealed class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TweakId { get; set; } = "";
    public string Category { get; set; } = "";
    public string UserSid { get; set; } = "";
    public DateTimeOffset Started { get; set; } = DateTimeOffset.UtcNow;
    public string Status { get; set; } = "Preparing";
    public bool Reboot { get; set; }
    public bool Undoable { get; set; } = true;
    public List<Change> Changes { get; set; } = [];
    public List<string> Messages { get; set; } = [];
    public string Error { get; set; } = "";
}
public sealed record Result(bool Success, string Status, string Message, bool Reboot = false, Session? Session = null);

public interface ISystemBackend
{
    Task<Reading> Read(Operation operation);
    Task Write(Operation operation);
    Task Restore(Operation operation, Reading reading);
    bool Equivalent(Operation operation, Reading left, Reading right);
}
