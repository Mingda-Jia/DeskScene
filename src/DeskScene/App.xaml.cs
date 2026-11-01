using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DeskScene;

public sealed record ProfileItem(Profile Profile, bool IsActive);

public partial class App : Application
{
    public static new App Current => (App)Application.Current;

    public Store Store { get; private set; } = null!;
    public Profile? ActiveProfile { get; private set; }

    /// <summary>Raised after profiles, settings or the active profile changed.</summary>
    public event Action? Changed;

    Mutex? instance;
    Tray tray = null!;
    Hotkeys hotkeys = null!;
    MainWindow? manager;
    string failedHotkeys = "";

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Single instance: a second launch just opens the manager of the running one.
        instance = new Mutex(true, "DeskScene.Instance", out var first);
        var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "DeskScene.Show");
        if (!first)
        {
            showSignal.Set();
            Shutdown();
            return;
        }
        ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => Dispatcher.BeginInvoke(ShowManager), null, -1, false);
        DispatcherUnhandledException += OnUnhandledException;

        Store = Store.Load(Path.Combine(Store.Folder, "profiles.json"), out var warning);
        Autostart.Refresh();
        hotkeys = new Hotkeys();
        tray = new Tray();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(Refresh);
        Refresh();

        if (warning != null) PromptWindow.Ask("Profiles file was reset", warning, ["OK"]);

        if (e.Args.Contains("--startup"))
        {
            if (Store.Profiles.FirstOrDefault(p => p.Name == Store.Settings.StartupProfile) is { } startup)
                await Switcher.Apply(startup);
        }
        else
        {
            ShowManager();
        }
    }

    /// <summary>Saves the store and refreshes everything that depends on it.</summary>
    public void Save()
    {
        Store.Save();
        Refresh();
    }

    /// <summary>Re-detects the active profile, re-registers hotkeys and notifies the UI.</summary>
    public void Refresh()
    {
        var now = TryCapture()?.Signature();
        ActiveProfile = Store.Profiles.FirstOrDefault(p => p.Signature() == now);

        List<string> failed = [];
        if (Store.Settings.Hotkeys)
            failed = hotkeys.Register(Store.Profiles.Where(p => !string.IsNullOrEmpty(p.Options.Hotkey))
                .Select(p => (p.Options.Hotkey!, (Action)(() => _ = Switcher.Apply(p)))));
        else
            hotkeys.Clear();
        var failedText = string.Join(", ", failed);
        if (failedText != "" && failedText != failedHotkeys)
            Notify("Hotkey not available", $"{failedText} is already used by another app.");
        failedHotkeys = failedText;

        Changed?.Invoke();
    }

    /// <summary>Pauses global hotkeys, e.g. while the user is typing a new one.</summary>
    public void SuspendHotkeys() => hotkeys.Clear();

    public static Profile? TryCapture()
    {
        try { return Profile.Capture("Current setup"); }
        catch (Exception) { return null; }
    }

    public void Notify(string title, string text) => tray.Notify(title, text);

    public void ShowManager()
    {
        if (manager == null)
        {
            manager = new MainWindow();
            manager.Closed += (_, _) => manager = null;
        }
        manager.Show();
        if (manager.WindowState == WindowState.Minimized) manager.WindowState = WindowState.Normal;
        manager.Activate();
    }

    public void Quit()
    {
        tray.Dispose();
        hotkeys.Dispose();
        Shutdown();
    }

    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        PromptWindow.Ask("Something went wrong", e.Exception.Message, ["OK"]);
    }
}
