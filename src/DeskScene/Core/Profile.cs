using System.Text.Json.Serialization;

namespace DeskScene;

public enum MissingAction { Ask, ApplyWithout, Cancel }

public enum TrayClick { Flyout, Manager }

public sealed class Settings
{
    public string? StartupProfile { get; set; }
    public TrayClick TrayClick { get; set; } = TrayClick.Flyout;
    public bool Hotkeys { get; set; } = true;
    public bool ConfirmChanges { get; set; } = true;
    public int RevertSeconds { get; set; } = 15;
    public MissingAction MissingDevice { get; set; } = MissingAction.Ask;
}

public sealed class ProfileOptions
{
    public string? Hotkey { get; set; }
    public bool ShowInTray { get; set; } = true;
    /// <summary>Null means "use the default from Settings".</summary>
    public MissingAction? MissingDevice { get; set; }
    public bool ConfirmChanges { get; set; } = true;

    /// <summary>ComboBox index: 0 = use default, then one entry per <see cref="MissingAction"/>.</summary>
    [JsonIgnore]
    public int MissingDeviceIndex
    {
        get => MissingDevice is { } m ? (int)m + 1 : 0;
        set => MissingDevice = value <= 0 ? null : (MissingAction)(value - 1);
    }
}

/// <summary>A monitor as recorded in a profile. <see cref="Path"/> indexes the raw CCD path array; -1 means connected but not shown.</summary>
public sealed record Monitor(
    string Id, string Name, string Connection, int Path,
    int Width = 0, int Height = 0, double Hz = 0, int X = 0, int Y = 0, int Rotation = 0,
    string? CloneOf = null, bool Hdr = false, int Scale = 0)
{
    [JsonIgnore] public bool Active => Path >= 0;
    [JsonIgnore] public bool Primary => Active && X == 0 && Y == 0;
    /// <summary>The monitor model from the device path (\\?\DISPLAY#MSI3DC0#…). Unlike the full path, it doesn't change with the port.</summary>
    [JsonIgnore] public string Model => Id.Split('#') is { Length: > 2 } parts ? parts[1] : Id;
    [JsonIgnore] public string Role => !Active ? "Off" : CloneOf != null ? $"Duplicate of {CloneOf}" : Primary ? "Main" : "Extend";
    [JsonIgnore] public string Resolution => Active ? $"{Width} × {Height}" : "—";
    [JsonIgnore] public string Refresh => Active ? $"{Hz:0.##} Hz" : "—";
    [JsonIgnore] public string ScaleText => Active && Scale > 0 ? $"{Scale}%" : "—";
    [JsonIgnore] public string HdrText => Active ? (Hdr ? "On" : "Off") : "—";
    [JsonIgnore] public string Orientation => !Active ? "—" : Rotation switch
    {
        90 => "Portrait",
        180 => "Landscape (flipped)",
        270 => "Portrait (flipped)",
        _ => "Landscape",
    };

    public string Describe() => !Active ? "Off" :
        $"{Width}×{Height} @ {Hz:0.##} Hz · {ScaleText}{(Hdr ? " · HDR" : "")}{(Rotation != 0 ? $" · {Orientation}" : "")} · {Role}" +
        (Role == "Extend" ? $" at {X},{Y}" : "");

    /// <summary>Pairs recorded monitor IDs with connected ones: exact device path first; otherwise, for a monitor moved to
    /// another port, the only unclaimed connected monitor of the same model.</summary>
    public static Dictionary<string, string> Match(IEnumerable<string> recorded, IEnumerable<string> connected)
    {
        var free = connected.ToHashSet();
        var pending = recorded.Distinct().ToList();
        var result = new Dictionary<string, string>();
        foreach (var id in pending.Where(free.Contains).ToList())
        {
            result[id] = id;
            free.Remove(id);
            pending.Remove(id);
        }
        foreach (var group in pending.GroupBy(ModelOf))
        {
            var candidates = free.Where(c => ModelOf(c) == group.Key).ToList();
            if (group.Count() == 1 && candidates.Count == 1) result[group.First()] = candidates[0];
        }
        return result;

        static string ModelOf(string id) => new Monitor(id, "", "", -1).Model;
    }
}

/// <summary>Raw CCD path/mode arrays (as recorded by Windows) plus the stable identities needed to re-apply them.</summary>
public sealed record DisplaySetup(byte[] Paths, byte[] Modes, Dictionary<long, string> Adapters, List<Monitor> Monitors);

public sealed record Endpoint(string Id, string Name);

public sealed record AudioSetup(Endpoint? Output, Endpoint? Communications, Endpoint? Input, int? Volume);

public sealed class Profile
{
    public string Name { get; set; } = "";
    public DateTime Updated { get; set; }
    public DisplaySetup Display { get; set; } = new([], [], [], []);
    public AudioSetup Audio { get; set; } = new(null, null, null, null);
    public ProfileOptions Options { get; set; } = new();

    public static Profile Capture(string name) => new()
    {
        Name = name,
        Updated = DateTime.Now,
        Display = DisplayService.Capture(),
        Audio = AudioService.Capture(),
    };

    [JsonIgnore] public IEnumerable<Monitor> ActiveMonitors => Display.Monitors.Where(m => m.Active);

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var active = ActiveMonitors.ToList();
            var mode = active.Count <= 1 ? "" : !active.Any(m => m.CloneOf != null) ? " · Extend"
                : active.Count(m => m.CloneOf == null) == 1 ? " · Duplicate" : " · Mixed";
            var audio = Audio.Output is { } o ? $" · {o.Name}" : "";
            return $"{active.Count} display{(active.Count == 1 ? "" : "s")}{mode}{audio}";
        }
    }

    /// <summary>Identity of the setup. Equal signatures mean "this profile is what Windows is showing now".
    /// Uses the monitor model (not the port-specific path) and ignores volume and connected-but-off monitors.</summary>
    public string Signature() =>
        string.Join(";", ActiveMonitors.OrderBy(m => m.Model, StringComparer.Ordinal).ThenBy(m => m.X).ThenBy(m => m.Y).Select(m =>
            $"{m.Model}|{m.Width}x{m.Height}@{Math.Round(m.Hz)}|{m.X},{m.Y}|{m.Rotation}|{m.CloneOf != null}|{m.Hdr}|{m.Scale}")) +
        $";{Audio.Output?.Id};{Audio.Communications?.Id};{Audio.Input?.Id}";
}

public static class ProfileDiff
{
    public static List<(string What, string Old, string New)> Compare(Profile old, Profile now)
    {
        var rows = new List<(string, string, string)>();
        var match = Monitor.Match(old.Display.Monitors.Select(m => m.Id), now.Display.Monitors.Select(m => m.Id));
        var pairs = old.Display.Monitors.Select(a => (Old: (Monitor?)a, New: now.Display.Monitors.FirstOrDefault(b => match.GetValueOrDefault(a.Id) == b.Id)))
            .Concat(now.Display.Monitors.Where(b => !match.ContainsValue(b.Id)).Select(b => (Old: (Monitor?)null, New: (Monitor?)b)));
        foreach (var (a, b) in pairs)
            Add((b ?? a)!.Name, a?.Describe() ?? "Off", b?.Describe() ?? "Off");
        Add("Audio output", old.Audio.Output?.Name, now.Audio.Output?.Name);
        Add("Communications", old.Audio.Communications?.Name, now.Audio.Communications?.Name);
        Add("Input", old.Audio.Input?.Name, now.Audio.Input?.Name);
        Add("Volume", old.Audio.Volume is int v1 ? $"{v1}%" : null, now.Audio.Volume is int v2 ? $"{v2}%" : null);
        return rows;

        void Add(string what, string? a, string? b)
        {
            if (a != b) rows.Add((what, a ?? "None", b ?? "None"));
        }
    }
}
