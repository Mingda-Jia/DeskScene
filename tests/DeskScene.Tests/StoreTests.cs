namespace DeskScene.Tests;

public sealed class StoreTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("deskscene-tests").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    [Fact]
    public void Round_trips_profiles_settings_and_raw_display_data()
    {
        var path = Path.Combine(dir, "profiles.json");
        var store = Store.Load(path, out _);
        store.Settings.MissingDevice = MissingAction.Cancel;
        store.Profiles.Add(new Profile
        {
            Name = "Desk",
            Display = new([1, 2, 3], [4, 5], new() { [42] = @"\\?\PCI#VEN_10DE" }, [new("id", "DELL", "DisplayPort", 0, 3840, 2160, 60, Scale: 150)]),
            Audio = new(new("{out}", "Speakers"), null, null, 35),
            Options = { Hotkey = "Ctrl+Alt+1", MissingDevice = MissingAction.ApplyWithout },
        });
        store.Save();

        var loaded = Store.Load(path, out var warning);

        Assert.Null(warning);
        Assert.Equal(MissingAction.Cancel, loaded.Settings.MissingDevice);
        var p = Assert.Single(loaded.Profiles);
        Assert.Equal("Desk", p.Name);
        Assert.Equal([1, 2, 3], p.Display.Paths);
        Assert.Equal(@"\\?\PCI#VEN_10DE", p.Display.Adapters[42]);
        Assert.Equal(new Monitor("id", "DELL", "DisplayPort", 0, 3840, 2160, 60, Scale: 150), p.Display.Monitors[0]);
        Assert.Equal(35, p.Audio.Volume);
        Assert.Equal("Ctrl+Alt+1", p.Options.Hotkey);
        Assert.Equal(MissingAction.ApplyWithout, p.Options.MissingDevice);
        Assert.Contains("\"Cancel\"", File.ReadAllText(path)); // enums are readable in the file
    }

    [Fact]
    public void Corrupt_file_is_moved_aside_and_store_starts_empty()
    {
        var path = Path.Combine(dir, "profiles.json");
        File.WriteAllText(path, "{ not json");

        var store = Store.Load(path, out var warning);

        Assert.Empty(store.Profiles);
        Assert.NotNull(warning);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(dir, "profiles.json.*.bak"));
    }

    [Fact]
    public void Import_replaces_profiles_with_the_same_name()
    {
        var exported = Store.Load(Path.Combine(dir, "export.json"), out _);
        exported.Profiles.Add(new Profile { Name = "TV", Options = { Hotkey = "Ctrl+Alt+2" } });
        exported.Profiles.Add(new Profile { Name = "Focus" });
        exported.Save();

        var store = Store.Load(Path.Combine(dir, "profiles.json"), out _);
        store.Profiles.Add(new Profile { Name = "tv" });
        store.Profiles.Add(new Profile { Name = "Desk" });

        Assert.Equal(2, store.Import(Path.Combine(dir, "export.json")));
        Assert.Equal(["Desk", "TV", "Focus"], store.Profiles.Select(p => p.Name));
        Assert.Equal("Ctrl+Alt+2", store.Profiles[1].Options.Hotkey);
    }
}
