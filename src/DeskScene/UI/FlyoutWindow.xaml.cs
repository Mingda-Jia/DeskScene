using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DeskScene;

/// <summary>Quick-switch list shown above the tray icon. Clicking a profile applies it.</summary>
public partial class FlyoutWindow : Window
{
    DateTime hiddenAt;

    public FlyoutWindow() => InitializeComponent();

    public void Toggle()
    {
        // Clicking the tray icon while the flyout is open first deactivates (hides) it; don't reopen it right away.
        if (IsVisible || (DateTime.Now - hiddenAt).TotalMilliseconds < 300)
        {
            Hide();
            return;
        }
        var app = App.Current;
        app.Refresh();
        ActiveText.Text = app.ActiveProfile is { } active ? $"Active: {active.Name}" : "Active: no saved profile";
        List.ItemsSource = app.Store.Profiles.Where(p => p.Options.ShowInTray)
            .Select(p => new ProfileItem(p, p == app.ActiveProfile)).ToList();

        Show();
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 12;
        Top = area.Bottom - ActualHeight - 12;
        Activate();
    }

    void Pick(object? item)
    {
        if (item is not ProfileItem picked) return;
        Hide();
        _ = Switcher.Apply(picked.Profile);
    }

    void OnPick(object sender, MouseButtonEventArgs e) => Pick(((ListBoxItem)sender).DataContext);

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Hide();
        else if (e.Key == Key.Enter) Pick(List.SelectedItem);
    }

    void OnOpenManager(object sender, RoutedEventArgs e)
    {
        Hide();
        App.Current.ShowManager();
    }

    void OnDeactivated(object? sender, EventArgs e)
    {
        Hide();
        hiddenAt = DateTime.Now;
    }
}
