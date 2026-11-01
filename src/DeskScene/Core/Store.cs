using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskScene;

/// <summary>Settings and profiles, persisted as one JSON file.</summary>
public sealed class Store
{
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskScene");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keeps names like "Desk · TV" readable; the file is never embedded in HTML
        Converters = { new JsonStringEnumConverter() },
    };

    public int Version { get; set; } = 1;
    public Settings Settings { get; set; } = new();
    public List<Profile> Profiles { get; set; } = [];

    [JsonIgnore] public string FilePath { get; private set; } = Path.Combine(Folder, "profiles.json");

    /// <summary>Loads the store. A corrupt file is moved aside (so nothing is lost) and an empty store is returned with a warning.</summary>
    public static Store Load(string path, out string? warning)
    {
        warning = null;
        Store store;
        try
        {
            store = File.Exists(path) ? JsonSerializer.Deserialize<Store>(File.ReadAllText(path), Json) ?? new() : new();
        }
        catch (JsonException)
        {
            var backup = $"{path}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";
            File.Move(path, backup);
            warning = $"The profiles file could not be read. It was moved to {backup} and DeskScene started with no profiles.";
            store = new();
        }
        store.FilePath = path;
        return store;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>Adds the profiles from an exported file; profiles with the same name are replaced.</summary>
    public int Import(string path)
    {
        var imported = JsonSerializer.Deserialize<Store>(File.ReadAllText(path), Json)?.Profiles ?? [];
        foreach (var p in imported)
        {
            Profiles.RemoveAll(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase));
            Profiles.Add(p);
        }
        return imported.Count;
    }
}
