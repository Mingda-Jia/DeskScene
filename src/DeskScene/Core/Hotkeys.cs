using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DeskScene;

/// <summary>System-wide hotkeys ("Ctrl+Alt+1") delivered to a hidden message-only window.</summary>
sealed class Hotkeys : IDisposable
{
    const int WmHotkey = 0x0312;
    const uint ModAlt = 1, ModControl = 2, ModShift = 4, ModWin = 8, ModNoRepeat = 0x4000;

    readonly HwndSource window = new(new HwndSourceParameters("DeskScene.Hotkeys") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
    readonly Dictionary<int, Action> actions = [];

    public Hotkeys() => window.AddHook(Hook);

    /// <summary>Replaces all registrations. Returns the hotkeys that could not be registered (invalid or used by another app).</summary>
    public List<string> Register(IEnumerable<(string Gesture, Action Action)> hotkeys)
    {
        Clear();
        var failed = new List<string>();
        foreach (var (gesture, action) in hotkeys)
        {
            var id = actions.Count + 1;
            if (TryParse(gesture, out var modifiers, out var key) && RegisterHotKey(window.Handle, id, modifiers | ModNoRepeat, key))
                actions[id] = action;
            else
                failed.Add(gesture);
        }
        return failed;
    }

    public void Clear()
    {
        foreach (var id in actions.Keys) UnregisterHotKey(window.Handle, id);
        actions.Clear();
    }

    /// <summary>Formats a key combination ("Ctrl+Alt+1"), or null if it isn't a valid global hotkey.</summary>
    public static string? Format(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.None) return null;
        try { return new KeyGestureConverter().ConvertToInvariantString(new KeyGesture(key, modifiers)); }
        catch (NotSupportedException) { return null; }
    }

    internal static bool TryParse(string gesture, out uint modifiers, out uint key)
    {
        modifiers = key = 0;
        try
        {
            var g = (KeyGesture)new KeyGestureConverter().ConvertFromInvariantString(gesture)!;
            if (g.Modifiers == ModifierKeys.None) return false;
            modifiers = (g.Modifiers.HasFlag(ModifierKeys.Alt) ? ModAlt : 0) | (g.Modifiers.HasFlag(ModifierKeys.Control) ? ModControl : 0) |
                        (g.Modifiers.HasFlag(ModifierKeys.Shift) ? ModShift : 0) | (g.Modifiers.HasFlag(ModifierKeys.Windows) ? ModWin : 0);
            key = (uint)KeyInterop.VirtualKeyFromKey(g.Key);
            return true;
        }
        catch (Exception e) when (e is NotSupportedException or ArgumentException or FormatException)
        {
            return false;
        }
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        window.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
