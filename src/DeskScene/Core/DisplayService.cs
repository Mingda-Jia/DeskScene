using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;

namespace DeskScene;

/// <summary>Reads and applies the Windows display topology through the CCD API (the same API Settings › Display uses).</summary>
static unsafe class DisplayService
{
    // Undocumented but long-stable: per-source DPI scaling, expressed relative to the recommended step.
    const int GetDpiScale = -3, SetDpiScale = -4;
    static readonly int[] DpiSteps = [100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500];

    [StructLayout(LayoutKind.Sequential)]
    struct DpiScaleGet { public DISPLAYCONFIG_DEVICE_INFO_HEADER Header; public int Min, Current, Max; }

    [StructLayout(LayoutKind.Sequential)]
    struct DpiScaleSet { public DISPLAYCONFIG_DEVICE_INFO_HEADER Header; public int Relative; }

    // Windows 11 24H2+ HDR requests (DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 / DISPLAYCONFIG_SET_HDR_STATE).
    const int GetColorInfo2 = 15, SetHdrState = 16, ColorModeHdr = 2;
    const uint HdrSupportedBit = 1 << 4;

    [StructLayout(LayoutKind.Sequential)]
    struct ColorInfo2 { public DISPLAYCONFIG_DEVICE_INFO_HEADER Header; public uint Value; public int ColorEncoding; public uint BitsPerColorChannel; public int ActiveColorMode; }

    [StructLayout(LayoutKind.Sequential)]
    struct HdrState { public DISPLAYCONFIG_DEVICE_INFO_HEADER Header; public uint Value; }

    public static DisplaySetup Capture()
    {
        var (paths, modes) = Query(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS);
        var adapters = new Dictionary<long, string>();
        var sources = new Dictionary<(long, uint), string>();
        var monitors = new List<Monitor>();

        for (int i = 0; i < paths.Length; i++)
        {
            var p = paths[i];
            foreach (var luid in new[] { p.sourceInfo.adapterId, p.targetInfo.adapterId })
                if (!adapters.ContainsKey(Long(luid))) adapters[Long(luid)] = AdapterPath(luid);

            var (id, name, connection) = Target(p.targetInfo.adapterId, p.targetInfo.id);
            var source = ModeAt(modes, p.sourceInfo.Anonymous.modeInfoIdx)?.Anonymous.sourceMode ?? default;
            var sync = ModeAt(modes, p.targetInfo.Anonymous.modeInfoIdx)?.Anonymous.targetMode.targetVideoSignalInfo.vSyncFreq ?? default;

            // Paths that share a source show the same image (Duplicate).
            var sourceKey = (Long(p.sourceInfo.adapterId), p.sourceInfo.id);
            sources.TryGetValue(sourceKey, out var cloneOf);
            sources.TryAdd(sourceKey, name);

            monitors.Add(new Monitor(id, name, connection, i,
                (int)source.width, (int)source.height,
                sync.Denominator == 0 ? 0 : Math.Round(sync.Numerator / (double)sync.Denominator, 2),
                source.position.x, source.position.y,
                Math.Max(0, ((int)p.targetInfo.rotation - 1) * 90),
                cloneOf,
                GetHdr(p.targetInfo.adapterId, p.targetInfo.id),
                GetScale(p.sourceInfo.adapterId, p.sourceInfo.id)));
        }

        foreach (var (id, (luid, targetId)) in Current().Targets)
            if (monitors.All(m => m.Id != id))
            {
                var t = Target(luid, targetId);
                monitors.Add(new Monitor(t.Id, t.Name, t.Connection, -1));
            }

        return new(ToBytes(paths), ToBytes(modes), adapters,
            [.. monitors.OrderBy(m => !m.Active).ThenBy(m => m.X).ThenBy(m => m.Y)]);
    }

    /// <summary>Recorded monitors that should be shown but aren't connected right now.</summary>
    public static List<Monitor> FindMissing(DisplaySetup setup)
    {
        var match = Match(setup, Current().Targets.Keys);
        return setup.Monitors.Where(m => m.Active && !match.ContainsKey(m.Id)).ToList();
    }

    static Dictionary<string, string> Match(DisplaySetup setup, IEnumerable<string> connected) =>
        Monitor.Match(setup.Monitors.Where(m => m.Active).Select(m => m.Id), connected);

    public static void Apply(DisplaySetup setup)
    {
        var paths = FromBytes<DISPLAYCONFIG_PATH_INFO>(setup.Paths);
        var modes = FromBytes<DISPLAYCONFIG_MODE_INFO>(setup.Modes);
        if (paths.Length == 0) throw new InvalidOperationException("None of the displays in this profile are connected.");

        // Adapter LUIDs change on every reboot / driver update, and a monitor may move to another port:
        // translate the recorded IDs to today's IDs using the stable device paths.
        var now = Current();
        var adapters = setup.Adapters
            .Where(a => now.Adapters.ContainsKey(a.Value))
            .ToDictionary(a => a.Key, a => now.Adapters[a.Value]);
        var targets = new Dictionary<(long, uint), (LUID, uint)>();
        foreach (var (recorded, connected) in Match(setup, now.Targets.Keys))
        {
            var p = paths[setup.Monitors.First(m => m.Id == recorded).Path];
            targets.TryAdd((Long(p.targetInfo.adapterId), p.targetInfo.id), now.Targets[connected]);
        }
        Remap(paths, modes, adapters, targets);

        fixed (DISPLAYCONFIG_PATH_INFO* pp = paths)
        fixed (DISPLAYCONFIG_MODE_INFO* pm = modes)
        {
            var flags = SET_DISPLAY_CONFIG_FLAGS.SDC_APPLY | SET_DISPLAY_CONFIG_FLAGS.SDC_USE_SUPPLIED_DISPLAY_CONFIG |
                        SET_DISPLAY_CONFIG_FLAGS.SDC_SAVE_TO_DATABASE | SET_DISPLAY_CONFIG_FLAGS.SDC_ALLOW_CHANGES;
            var result = (int)PInvoke.SetDisplayConfig((uint)paths.Length, pp, (uint)modes.Length, pm, flags);
            if (result != 0)
                throw new Win32Exception(result, $"Windows rejected this display layout: {new Win32Exception(result).Message}");
        }

        ApplyHdrAndScale(setup);
    }

    /// <summary>Returns a copy of the setup without the given monitors, with mode indices compacted.</summary>
    internal static DisplaySetup Without(DisplaySetup setup, ICollection<string> ids)
    {
        var paths = FromBytes<DISPLAYCONFIG_PATH_INFO>(setup.Paths);
        var modes = FromBytes<DISPLAYCONFIG_MODE_INFO>(setup.Modes);
        var keep = setup.Monitors.Where(m => m.Active && !ids.Contains(m.Id)).OrderBy(m => m.Path).ToList();
        var newPaths = new List<DISPLAYCONFIG_PATH_INFO>();
        var newModes = new List<DISPLAYCONFIG_MODE_INFO>();
        var modeMap = new Dictionary<uint, uint>();
        var monitors = new List<Monitor>();

        foreach (var m in keep)
        {
            var p = paths[m.Path];
            p.sourceInfo.Anonymous.modeInfoIdx = Reindex(p.sourceInfo.Anonymous.modeInfoIdx);
            p.targetInfo.Anonymous.modeInfoIdx = Reindex(p.targetInfo.Anonymous.modeInfoIdx);
            monitors.Add(m with { Path = newPaths.Count });
            newPaths.Add(p);
        }
        monitors.AddRange(setup.Monitors.Except(keep).Select(m => m with { Path = -1 }));

        // If the main display was dropped, make the first remaining display the main one (at 0,0).
        var sourceModes = newModes.Where(m => m.infoType == DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE).ToList();
        if (sourceModes.Count > 0 && !sourceModes.Any(m => m.Anonymous.sourceMode.position.x == 0 && m.Anonymous.sourceMode.position.y == 0))
        {
            var origin = sourceModes[0].Anonymous.sourceMode.position;
            for (int i = 0; i < newModes.Count; i++)
            {
                if (newModes[i].infoType != DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE) continue;
                var mode = newModes[i];
                mode.Anonymous.sourceMode.position.x -= origin.x;
                mode.Anonymous.sourceMode.position.y -= origin.y;
                newModes[i] = mode;
            }
        }

        return setup with { Paths = ToBytes(newPaths.ToArray()), Modes = ToBytes(newModes.ToArray()), Monitors = monitors };

        uint Reindex(uint index)
        {
            if (index >= modes.Length) return index; // DISPLAYCONFIG_PATH_MODE_IDX_INVALID
            if (!modeMap.TryGetValue(index, out var mapped))
            {
                mapped = modeMap[index] = (uint)newModes.Count;
                newModes.Add(modes[index]);
            }
            return mapped;
        }
    }

    /// <summary>Rewrites recorded adapter and target IDs to the current ones.</summary>
    internal static void Remap(DISPLAYCONFIG_PATH_INFO[] paths, DISPLAYCONFIG_MODE_INFO[] modes,
        IReadOnlyDictionary<long, LUID> adapters, IReadOnlyDictionary<(long Adapter, uint Id), (LUID Adapter, uint Id)> targets)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            ref var p = ref paths[i];
            if (targets.TryGetValue((Long(p.targetInfo.adapterId), p.targetInfo.id), out var t))
                (p.targetInfo.adapterId, p.targetInfo.id) = t;
            else
                p.targetInfo.adapterId = Map(p.targetInfo.adapterId);
            p.sourceInfo.adapterId = Map(p.sourceInfo.adapterId);
        }
        for (int i = 0; i < modes.Length; i++)
        {
            ref var m = ref modes[i];
            if (m.infoType == DISPLAYCONFIG_MODE_INFO_TYPE.DISPLAYCONFIG_MODE_INFO_TYPE_TARGET &&
                targets.TryGetValue((Long(m.adapterId), m.id), out var t))
                (m.adapterId, m.id) = t;
            else
                m.adapterId = Map(m.adapterId);
        }

        LUID Map(LUID luid) => adapters.TryGetValue(Long(luid), out var mapped) ? mapped : luid;
    }

    static void ApplyHdrAndScale(DisplaySetup setup)
    {
        var (paths, _) = Query(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS);
        var current = paths.Select(p => (Path: p, Id: Target(p.targetInfo.adapterId, p.targetInfo.id).Id)).ToList();
        foreach (var (recorded, connected) in Match(setup, current.Select(c => c.Id)))
        {
            var m = setup.Monitors.First(w => w.Id == recorded);
            var p = current.First(c => c.Id == connected).Path;
            if (GetHdr(p.targetInfo.adapterId, p.targetInfo.id) != m.Hdr) SetHdr(p.targetInfo.adapterId, p.targetInfo.id, m.Hdr);
            if (m.Scale > 0 && GetScale(p.sourceInfo.adapterId, p.sourceInfo.id) != m.Scale) SetScale(p.sourceInfo.adapterId, p.sourceInfo.id, m.Scale);
        }
    }

    /// <summary>Adapters (by device path) and connected monitors (by device path) as Windows sees them right now.</summary>
    static (Dictionary<string, LUID> Adapters, Dictionary<string, (LUID, uint)> Targets) Current()
    {
        var (paths, _) = Query(QUERY_DISPLAY_CONFIG_FLAGS.QDC_ALL_PATHS);
        var adapters = new Dictionary<string, LUID>();
        var targets = new Dictionary<string, (LUID, uint)>();
        var seen = new HashSet<(long, uint)>();
        foreach (var p in paths)
        {
            foreach (var luid in new[] { p.sourceInfo.adapterId, p.targetInfo.adapterId })
                adapters.TryAdd(AdapterPath(luid), luid);
            if (p.targetInfo.targetAvailable && seen.Add((Long(p.targetInfo.adapterId), p.targetInfo.id)))
            {
                var id = Target(p.targetInfo.adapterId, p.targetInfo.id).Id;
                if (id != "") targets.TryAdd(id, (p.targetInfo.adapterId, p.targetInfo.id));
            }
        }
        return (adapters, targets);
    }

    static (DISPLAYCONFIG_PATH_INFO[], DISPLAYCONFIG_MODE_INFO[]) Query(QUERY_DISPLAY_CONFIG_FLAGS flags)
    {
        while (true)
        {
            uint pathCount, modeCount;
            Check((int)PInvoke.GetDisplayConfigBufferSizes(flags, &pathCount, &modeCount));
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            fixed (DISPLAYCONFIG_PATH_INFO* pp = paths)
            fixed (DISPLAYCONFIG_MODE_INFO* pm = modes)
            {
                var result = (int)PInvoke.QueryDisplayConfig(flags, &pathCount, pp, &modeCount, pm, null);
                if (result == 122) continue; // ERROR_INSUFFICIENT_BUFFER: the setup changed between the two calls
                Check(result);
            }
            return (paths[..(int)pathCount], modes[..(int)modeCount]);
        }
    }

    static (string Id, string Name, string Connection) Target(LUID adapter, uint id)
    {
        var t = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = Header(DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME, sizeof(DISPLAYCONFIG_TARGET_DEVICE_NAME), adapter, id),
        };
        if ((int)PInvoke.DisplayConfigGetDeviceInfo(&t.header) != 0) return ("", "Display", "");
        var connection = Connection((int)t.outputTechnology);
        var name = t.monitorFriendlyDeviceName.ToString();
        if (name == "") name = connection == "Internal" ? "Built-in display" : "Display";
        return (t.monitorDevicePath.ToString(), name, connection);
    }

    static string AdapterPath(LUID adapter)
    {
        var a = new DISPLAYCONFIG_ADAPTER_NAME
        {
            header = Header(DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADAPTER_NAME, sizeof(DISPLAYCONFIG_ADAPTER_NAME), adapter, 0),
        };
        return (int)PInvoke.DisplayConfigGetDeviceInfo(&a.header) == 0 ? a.adapterDevicePath.ToString() : "";
    }

    static bool GetHdr(LUID adapter, uint target)
    {
        // Windows 11 24H2+: HDR is reported separately from wide color / auto color management.
        var info = new ColorInfo2 { Header = Header((DISPLAYCONFIG_DEVICE_INFO_TYPE)GetColorInfo2, sizeof(ColorInfo2), adapter, target) };
        if ((int)PInvoke.DisplayConfigGetDeviceInfo(&info.Header) == 0)
            return (info.Value & HdrSupportedBit) != 0 && info.ActiveColorMode == ColorModeHdr;

        var c = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            header = Header(DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO, sizeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO), adapter, target),
        };
        // Older builds: "advanced color" means HDR. Bit 0: supported, bit 1: enabled.
        return (int)PInvoke.DisplayConfigGetDeviceInfo(&c.header) == 0 && (c.Anonymous.value & 0b11) == 0b11;
    }

    static void SetHdr(LUID adapter, uint target, bool enabled)
    {
        var state = new HdrState { Header = Header((DISPLAYCONFIG_DEVICE_INFO_TYPE)SetHdrState, sizeof(HdrState), adapter, target), Value = enabled ? 1u : 0u };
        if ((int)PInvoke.DisplayConfigSetDeviceInfo(&state.Header) == 0) return;

        var s = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
        {
            header = Header(DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE, sizeof(DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE), adapter, target),
        };
        s.Anonymous.value = enabled ? 1u : 0u;
        PInvoke.DisplayConfigSetDeviceInfo(&s.header);
    }

    static int GetScale(LUID adapter, uint source)
    {
        var d = new DpiScaleGet { Header = Header((DISPLAYCONFIG_DEVICE_INFO_TYPE)GetDpiScale, sizeof(DpiScaleGet), adapter, source) };
        if ((int)PInvoke.DisplayConfigGetDeviceInfo(&d.Header) != 0) return 0;
        var index = Math.Abs(d.Min) + d.Current; // Math.Abs(Min) is the recommended step
        return index >= 0 && index < DpiSteps.Length ? DpiSteps[index] : 0;
    }

    static void SetScale(LUID adapter, uint source, int percent)
    {
        var d = new DpiScaleGet { Header = Header((DISPLAYCONFIG_DEVICE_INFO_TYPE)GetDpiScale, sizeof(DpiScaleGet), adapter, source) };
        var step = Array.IndexOf(DpiSteps, percent);
        if (step < 0 || (int)PInvoke.DisplayConfigGetDeviceInfo(&d.Header) != 0) return;
        var s = new DpiScaleSet { Header = Header((DISPLAYCONFIG_DEVICE_INFO_TYPE)SetDpiScale, sizeof(DpiScaleSet), adapter, source), Relative = step - Math.Abs(d.Min) };
        PInvoke.DisplayConfigSetDeviceInfo(&s.Header);
    }

    static DISPLAYCONFIG_DEVICE_INFO_HEADER Header(DISPLAYCONFIG_DEVICE_INFO_TYPE type, int size, LUID adapter, uint id) =>
        new() { type = type, size = (uint)size, adapterId = adapter, id = id };

    static DISPLAYCONFIG_MODE_INFO? ModeAt(DISPLAYCONFIG_MODE_INFO[] modes, uint index) => index < modes.Length ? modes[index] : null;

    static string Connection(int technology) => technology switch
    {
        0 => "VGA",
        4 => "DVI",
        5 => "HDMI",
        10 => "DisplayPort",
        6 or 11 or 13 or int.MinValue => "Internal",
        15 => "Wireless",
        16 or 17 => "Virtual",
        18 => "USB-C",
        _ => "Other",
    };

    internal static long Long(LUID luid) => ((long)luid.HighPart << 32) | luid.LowPart;

    internal static byte[] ToBytes<T>(T[] items) where T : unmanaged => MemoryMarshal.AsBytes(items.AsSpan()).ToArray();

    internal static T[] FromBytes<T>(byte[] bytes) where T : unmanaged => MemoryMarshal.Cast<byte, T>(bytes).ToArray();

    static void Check(int result)
    {
        if (result != 0) throw new Win32Exception(result);
    }
}
