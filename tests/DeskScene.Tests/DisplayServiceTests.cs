using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;

namespace DeskScene.Tests;

public class DisplayServiceTests
{
    static readonly LUID OldGpu = new() { LowPart = 0x1111 }, NewGpu = new() { LowPart = 0x2222 };

    static DISPLAYCONFIG_PATH_INFO Path(LUID adapter, uint source, uint target, uint sourceMode, uint targetMode)
    {
        var p = new DISPLAYCONFIG_PATH_INFO();
        p.sourceInfo.adapterId = adapter;
        p.sourceInfo.id = source;
        p.sourceInfo.Anonymous.modeInfoIdx = sourceMode;
        p.targetInfo.adapterId = adapter;
        p.targetInfo.id = target;
        p.targetInfo.Anonymous.modeInfoIdx = targetMode;
        return p;
    }

    static DISPLAYCONFIG_MODE_INFO SourceMode(LUID adapter, uint id, int x, int y)
    {
        var m = new DISPLAYCONFIG_MODE_INFO { infoType = DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE, adapterId = adapter, id = id };
        m.Anonymous.sourceMode.position.x = x;
        m.Anonymous.sourceMode.position.y = y;
        return m;
    }

    static DISPLAYCONFIG_MODE_INFO TargetMode(LUID adapter, uint id) =>
        new() { infoType = DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET, adapterId = adapter, id = id };

    [Fact]
    public void Remap_moves_adapters_and_follows_monitors_to_new_ports()
    {
        var paths = new[] { Path(OldGpu, 0, 100, 0, 1), Path(OldGpu, 1, 101, 2, 3) };
        var modes = new[] { SourceMode(OldGpu, 0, 0, 0), TargetMode(OldGpu, 100), SourceMode(OldGpu, 1, 2560, 0), TargetMode(OldGpu, 101) };
        var adapters = new Dictionary<long, LUID> { [DisplayService.Long(OldGpu)] = NewGpu };
        // The monitor on target 101 is now plugged into the port with target id 205.
        var targets = new Dictionary<(long, uint), (LUID, uint)> { [(DisplayService.Long(OldGpu), 101)] = (NewGpu, 205) };

        DisplayService.Remap(paths, modes, adapters, targets);

        Assert.All(paths, p => Assert.Equal(NewGpu, p.sourceInfo.adapterId));
        Assert.All(paths, p => Assert.Equal(NewGpu, p.targetInfo.adapterId));
        Assert.All(modes, m => Assert.Equal(NewGpu, m.adapterId));
        Assert.Equal([100u, 205u], paths.Select(p => p.targetInfo.id));
        Assert.Equal(205u, modes[3].id);
        Assert.Equal(1u, modes[2].id); // source ids are untouched
    }

    [Fact]
    public void Without_drops_missing_monitors_and_compacts_modes()
    {
        // TV (path 1) is the main display; the desk monitor (path 0) is extended to its left.
        var paths = new[] { Path(OldGpu, 0, 100, 0, 1), Path(OldGpu, 1, 101, 2, 3) };
        var modes = new[] { SourceMode(OldGpu, 0, -2560, 0), TargetMode(OldGpu, 100), SourceMode(OldGpu, 1, 0, 0), TargetMode(OldGpu, 101) };
        var setup = new DisplaySetup(DisplayService.ToBytes(paths), DisplayService.ToBytes(modes), [],
            [new("desk", "Desk", "DisplayPort", 0, X: -2560), new("tv", "TV", "HDMI", 1)]);

        var result = DisplayService.Without(setup, ["tv"]);

        var newPaths = DisplayService.FromBytes<DISPLAYCONFIG_PATH_INFO>(result.Paths);
        var newModes = DisplayService.FromBytes<DISPLAYCONFIG_MODE_INFO>(result.Modes);
        var path = Assert.Single(newPaths);
        Assert.Equal(100u, path.targetInfo.id);
        Assert.Equal(2, newModes.Length);
        Assert.Equal(0u, path.sourceInfo.Anonymous.modeInfoIdx);
        Assert.Equal(1u, path.targetInfo.Anonymous.modeInfoIdx);
        // The remaining display becomes the main one at (0,0).
        Assert.Equal(0, newModes[0].Anonymous.sourceMode.position.x);
        Assert.Equal([0, -1], result.Monitors.Select(m => m.Path));
    }
}
