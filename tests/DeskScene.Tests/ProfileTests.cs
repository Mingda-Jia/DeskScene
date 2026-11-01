namespace DeskScene.Tests;

public class ProfileTests
{
    static Monitor Mon(string id, int path, int x = 0, string? cloneOf = null, int width = 2560) =>
        new(id, id.ToUpperInvariant(), "HDMI", path, width, 1440, 59.95, x, 0, 0, cloneOf, false, 100);

    static Profile Make(params Monitor[] monitors) => new()
    {
        Name = "p",
        Display = new([], [], [], [.. monitors]),
        Audio = new(new("{out}", "Speakers (Realtek(R) Audio)"), null, new("{in}", "Mic"), 40),
    };

    [Fact]
    public void Signature_ignores_order_volume_and_monitors_that_are_off()
    {
        var a = Make(Mon("a", 0), Mon("b", 1, x: 2560));
        var b = Make(Mon("b", 0, x: 2560), Mon("a", 1), Mon("tv", -1));
        b.Audio = b.Audio with { Volume = 80 };

        Assert.Equal(a.Signature(), b.Signature());
    }

    [Fact]
    public void Signature_changes_when_layout_or_audio_changes()
    {
        var a = Make(Mon("a", 0), Mon("b", 1, x: 2560));

        Assert.NotEqual(a.Signature(), Make(Mon("a", 0), Mon("b", 1, x: -2560)).Signature());
        var otherAudio = Make(Mon("a", 0), Mon("b", 1, x: 2560));
        otherAudio.Audio = otherAudio.Audio with { Output = new("{tv}", "LG TV") };
        Assert.NotEqual(a.Signature(), otherAudio.Signature());
    }

    [Fact]
    public void Summary_describes_displays_mode_and_audio()
    {
        Assert.Equal("2 displays · Extend · Speakers (Realtek(R) Audio)", Make(Mon("a", 0), Mon("b", 1, x: 2560)).Summary);
        Assert.Equal("2 displays · Duplicate · Speakers (Realtek(R) Audio)", Make(Mon("a", 0), Mon("tv", 1, cloneOf: "A")).Summary);
        Assert.Equal("1 display · Speakers (Realtek(R) Audio)", Make(Mon("a", 0), Mon("b", -1)).Summary);
    }

    [Fact]
    public void Roles_follow_position_and_cloning()
    {
        Assert.Equal("Main", Mon("a", 0).Role);
        Assert.Equal("Extend", Mon("b", 1, x: 2560).Role);
        Assert.Equal("Duplicate of A", Mon("tv", 2, cloneOf: "A").Role);
        Assert.Equal("Off", Mon("x", -1).Role);
    }

    [Fact]
    public void Diff_lists_only_what_changed()
    {
        var old = Make(Mon("a", 0), Mon("b", 1, x: 2560, width: 1920));
        var now = Make(Mon("a", 0), Mon("b", 1, x: 2560));
        now.Audio = now.Audio with { Output = new("{tv}", "LG TV"), Volume = 55 };

        var rows = ProfileDiff.Compare(old, now);

        Assert.Equal(["B", "Audio output", "Volume"], rows.Select(r => r.What));
        Assert.Contains("1920×1440", rows[0].Old);
        Assert.Contains("2560×1440", rows[0].New);
        Assert.Equal(("Volume", "40%", "55%"), rows[2]);
    }

    [Fact]
    public void Diff_is_empty_for_identical_profiles()
    {
        Assert.Empty(ProfileDiff.Compare(Make(Mon("a", 0)), Make(Mon("a", 0))));
    }

    const string Msi = @"\\?\DISPLAY#MSI3DC0#5&243be955&0&UID176387#{e6f07b5f}";
    const string LenPortA = @"\\?\DISPLAY#LEN62A2#5&243be955&0&UID176385#{e6f07b5f}";
    const string LenPortB = @"\\?\DISPLAY#LEN62A2#5&243be955&0&UID260#{e6f07b5f}";
    const string LenPortC = @"\\?\DISPLAY#LEN62A2#5&243be955&0&UID256#{e6f07b5f}";

    [Fact]
    public void Match_follows_a_monitor_to_another_port()
    {
        var match = Monitor.Match([Msi, LenPortA], [Msi, LenPortB]);

        Assert.Equal(Msi, match[Msi]);
        Assert.Equal(LenPortB, match[LenPortA]);
    }

    [Fact]
    public void Match_refuses_to_guess_between_identical_models()
    {
        // Two identical monitors both moved ports: we can't tell which is which.
        Assert.Empty(Monitor.Match([LenPortA, LenPortC], [LenPortB, @"\\?\DISPLAY#LEN62A2#5&243be955&0&UID999#{e6f07b5f}"]));
        // Only one recorded, but two candidates connected: also ambiguous.
        Assert.Empty(Monitor.Match([LenPortA], [LenPortB, LenPortC]));
    }

    [Fact]
    public void Moving_a_monitor_to_another_port_keeps_signature_and_diff_stable()
    {
        var before = Make(Mon(Msi, 0), Mon(LenPortA, 1, x: 2560));
        var after = Make(Mon(Msi, 0), Mon(LenPortB, 1, x: 2560));

        Assert.Equal(before.Signature(), after.Signature());
        Assert.Empty(ProfileDiff.Compare(before, after));
    }

    [Fact]
    public void Missing_device_index_maps_to_default_and_actions()
    {
        var o = new ProfileOptions();
        Assert.Equal(0, o.MissingDeviceIndex);
        o.MissingDeviceIndex = 2;
        Assert.Equal(MissingAction.ApplyWithout, o.MissingDevice);
        o.MissingDeviceIndex = 0;
        Assert.Null(o.MissingDevice);
    }
}
