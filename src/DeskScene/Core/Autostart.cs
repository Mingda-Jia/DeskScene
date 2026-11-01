using Microsoft.Win32;

namespace DeskScene;

/// <summary>"Start with Windows" via the per-user Run key (no admin rights needed).</summary>
static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "DeskScene";

    static string Command => $"\"{Environment.ProcessPath}\" --startup";

    public static bool Enabled
    {
        get => Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(Name) is string;
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(Name, Command);
            else key.DeleteValue(Name, throwOnMissingValue: false);
        }
    }

    /// <summary>Keeps the registered path current if the exe was moved.</summary>
    public static void Refresh()
    {
        if (Enabled) Enabled = true;
    }
}
