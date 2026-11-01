using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace DeskScene;

/// <summary>Tray icon: left-click opens the quick-switch flyout (or the manager), right-click shows the switch menu.</summary>
sealed class Tray : IDisposable
{
    readonly Forms.NotifyIcon icon;
    FlyoutWindow? flyout;

    public Tray()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/icon.ico")).Stream;
        icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize),
            Text = "DeskScene",
            Visible = true,
        };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) OnLeftClick();
            else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
        };
        App.Current.Changed += UpdateTooltip;
    }

    public void Notify(string title, string text) =>
        icon.ShowBalloonTip(3000, title, string.IsNullOrEmpty(text) ? " " : text, Forms.ToolTipIcon.None);

    void UpdateTooltip()
    {
        var text = App.Current.ActiveProfile is { } p ? $"DeskScene\nActive: {p.Name}" : "DeskScene";
        icon.Text = text.Length > 127 ? text[..127] : text;
    }

    void OnLeftClick()
    {
        var app = App.Current;
        if (app.Store.Settings.TrayClick == TrayClick.Manager)
        {
            app.ShowManager();
            return;
        }
        flyout ??= new FlyoutWindow();
        flyout.Toggle();
    }

    void ShowMenu()
    {
        var app = App.Current;
        app.Refresh();
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        menu.Items.Add(new MenuItem { Header = "Profiles", IsEnabled = false });
        foreach (var p in app.Store.Profiles.Where(p => p.Options.ShowInTray))
            menu.Items.Add(Item(p.Name, () => _ = Switcher.Apply(p), p == app.ActiveProfile, p.Options.Hotkey));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Open manager", app.ShowManager));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Exit", app.Quit));
        // A menu without an owner window only closes on outside clicks if it is the foreground window.
        menu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(menu) is HwndSource source) SetForegroundWindow(source.Handle);
        };
        menu.IsOpen = true;
    }

    static MenuItem Item(string text, Action click, bool isChecked = false, string? gesture = null)
    {
        var item = new MenuItem { Header = text, IsCheckable = isChecked, IsChecked = isChecked, InputGestureText = gesture ?? "" };
        item.Click += (_, _) => click();
        return item;
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);
}
