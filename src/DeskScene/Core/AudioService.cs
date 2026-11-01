using System.Runtime.InteropServices;

namespace DeskScene;

/// <summary>Reads and sets the default audio devices and output volume.</summary>
static class AudioService
{
    const int DeviceStateActive = 1, ClsctxAll = 23, VtLpwstr = 31;
    static readonly PropertyKey FriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public static AudioSetup Capture()
    {
        var devices = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        var output = Default(devices, DataFlow.Render, Role.Console);
        return new(output,
            Default(devices, DataFlow.Render, Role.Communications),
            Default(devices, DataFlow.Capture, Role.Console),
            output == null ? null : (int)Math.Round(Volume(devices, output.Id).GetVolume() * 100));
    }

    /// <summary>Sets the recorded defaults and volume. Endpoints that appear late (e.g. a TV's HDMI audio right after
    /// the TV display is switched on) get a few seconds to show up. Returns the names of endpoints that never did.</summary>
    public static async Task<List<string>> ApplyAsync(AudioSetup audio)
    {
        var wanted = new (Endpoint? Endpoint, Role[] Roles)[]
        {
            (audio.Output, [Role.Console, Role.Multimedia]),
            (audio.Communications, [Role.Communications]),
            (audio.Input, [Role.Console, Role.Multimedia]),
        }.Where(w => w.Endpoint != null).ToList();

        var devices = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        for (int i = 0; i < 20 && !wanted.All(w => IsActive(devices, w.Endpoint!.Id)); i++)
            await Task.Delay(250);

        var policy = (IPolicyConfig)new PolicyConfigClient();
        var missing = new List<string>();
        foreach (var (endpoint, roles) in wanted)
        {
            if (!IsActive(devices, endpoint!.Id)) { missing.Add(endpoint.Name); continue; }
            foreach (var role in roles) policy.SetDefaultEndpoint(endpoint.Id, role);
        }
        if (audio.Output is { } output && audio.Volume is int volume && IsActive(devices, output.Id))
        {
            var context = Guid.Empty;
            Volume(devices, output.Id).SetMasterVolumeLevelScalar(volume / 100f, ref context);
        }
        return missing;
    }

    static Endpoint? Default(IMMDeviceEnumerator devices, DataFlow flow, Role role)
    {
        if (devices.GetDefaultAudioEndpoint(flow, role, out var device) != 0) return null;
        device.GetId(out var id);
        return new(id, Name(device));
    }

    static bool IsActive(IMMDeviceEnumerator devices, string id)
    {
        if (devices.GetDevice(id, out var device) != 0) return false;
        device.GetState(out var state);
        return state == DeviceStateActive;
    }

    static string Name(IMMDevice device)
    {
        device.OpenPropertyStore(0, out var store);
        var key = FriendlyName;
        store.GetValue(ref key, out var value);
        try { return value.Type == VtLpwstr ? Marshal.PtrToStringUni(value.Value) ?? "" : ""; }
        finally { PropVariantClear(ref value); }
    }

    static IAudioEndpointVolume Volume(IMMDeviceEnumerator devices, string id)
    {
        devices.GetDevice(id, out var device);
        var iid = typeof(IAudioEndpointVolume).GUID;
        device.Activate(ref iid, ClsctxAll, IntPtr.Zero, out var volume);
        return (IAudioEndpointVolume)volume;
    }

    static float GetVolume(this IAudioEndpointVolume volume)
    {
        volume.GetMasterVolumeLevelScalar(out var level);
        return level;
    }

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PropVariant value);
}
