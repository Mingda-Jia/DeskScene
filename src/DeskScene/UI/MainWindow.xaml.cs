using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace DeskScene;

public sealed record MonitorRow(string Number, Monitor Monitor);

/// <summary>Profile manager: list on the left; the selected profile, the live setup, or settings on the right.</summary>
public partial class MainWindow : Window
{
    enum Page { Profile, Live, Settings }

    static readonly int[] RevertChoices = [10, 15, 30, 60];

    readonly App app = App.Current;
    Page page = Page.Profile;
    bool updating;

    public MainWindow()
    {
        InitializeComponent();
        RevertBox.ItemsSource = RevertChoices.Select(s => $"{s} seconds").ToList();
        app.Changed += OnChanged;
        Closed += (_, _) => app.Changed -= OnChanged;
        OnChanged();
    }

    Profile? Selected => (ProfileList.SelectedItem as ProfileItem)?.Profile;

    void OnChanged()
    {
        var keep = Selected ?? app.ActiveProfile;
        var items = app.Store.Profiles.Select(p => new ProfileItem(p, p == app.ActiveProfile)).ToList();
        updating = true;
        ProfileList.ItemsSource = items;
        ProfileList.SelectedItem = page == Page.Profile ? items.FirstOrDefault(i => i.Profile == keep) ?? items.FirstOrDefault() : null;
        updating = false;
        ShowPage(page);
    }

    void ShowPage(Page next)
    {
        page = next;
        var profile = next == Page.Live ? App.TryCapture() : next == Page.Profile ? Selected : null;
        EmptyPanel.Visibility = Show(next == Page.Profile && profile == null);
        ProfilePanel.Visibility = Show(profile != null);
        SettingsPanel.Visibility = Show(next == Page.Settings);
        if (next == Page.Settings) LoadSettings();
        if (profile == null) return;

        var live = next == Page.Live;
        ProfilePanel.DataContext = profile;
        Actions.Visibility = OptionsCard.Visibility = Meta.Visibility = Show(!live);
        Meta.Text = $"Last updated {profile.Updated:g}";
        InfoText.Text = live ? "This is how Windows is set up right now. Save it as a profile to switch back to it later."
            : profile == app.ActiveProfile ? "Active: this is how Windows is set up right now."
            : $"Not active. Currently applied: {app.ActiveProfile?.Name ?? "no saved profile"}.";
        Table.ItemsSource = profile.Display.Monitors.Select((m, i) => new MonitorRow(m.Active ? $"{i + 1}" : "", m)).ToList();
    }

    static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    void OnSelect(object sender, SelectionChangedEventArgs e)
    {
        if (!updating && Selected != null) ShowPage(Page.Profile);
    }

    void OnLive(object sender, RoutedEventArgs e) => Navigate(Page.Live);

    void OnSettings(object sender, RoutedEventArgs e) => Navigate(Page.Settings);

    void Navigate(Page next)
    {
        updating = true;
        ProfileList.SelectedItem = null;
        updating = false;
        ShowPage(next);
    }

    void Select(Profile profile)
    {
        page = Page.Profile;
        ProfileList.SelectedItem = ((List<ProfileItem>)ProfileList.ItemsSource).FirstOrDefault(i => i.Profile == profile);
    }

    // ---- Profile actions ----

    async void OnApply(object sender, RoutedEventArgs e)
    {
        if (Selected is { } p) await Switcher.Apply(p);
    }

    void OnSaveNew(object sender, RoutedEventArgs e)
    {
        var snapshot = Profile.Capture("");
        var names = app.Store.Profiles.Select(p => p.Name).ToList();
        var name = Enumerable.Range(1, 1000).Select(i => $"Profile {i}").First(n => !names.Contains(n));
        var dialog = new SaveDialog(snapshot, name, names) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        snapshot.Name = dialog.ProfileName;
        snapshot.Options.Hotkey = dialog.Hotkey;
        app.Store.Profiles.Add(snapshot);
        app.Save();
        Select(snapshot);
    }

    void OnUpdate(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        var now = Profile.Capture(p.Name);
        if (PromptWindow.Ask($"Update \"{p.Name}\"?", DiffView(ProfileDiff.Compare(p, now)), ["Update", "Cancel"]) != 0) return;
        p.Display = now.Display;
        p.Audio = now.Audio;
        p.Updated = now.Updated;
        app.Save();
    }

    void OnRename(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        var dialog = new SaveDialog(null, p.Name, app.Store.Profiles.Where(x => x != p).Select(x => x.Name)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (app.Store.Settings.StartupProfile == p.Name) app.Store.Settings.StartupProfile = dialog.ProfileName;
        p.Name = dialog.ProfileName;
        app.Save();
    }

    void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } p) return;
        if (PromptWindow.Ask("Delete profile?", $"\"{p.Name}\" will be permanently removed. Your current display settings won't change.",
                ["Delete", "Cancel"]) != 0) return;
        app.Store.Profiles.Remove(p);
        app.Save();
    }

    void OnOptionChanged(object? sender, DataTransferEventArgs e) => app.Save();

    static TextBlock DiffView(List<(string What, string Old, string New)> changes)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 22 };
        text.Inlines.Add(changes.Count == 0
            ? "Nothing has changed since this profile was saved."
            : "The profile will be overwritten with your current setup. Changes:");
        foreach (var (what, old, now) in changes)
        {
            text.Inlines.Add(new LineBreak());
            text.Inlines.Add(new Run($"{what}:  ") { FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new Run(old) { TextDecorations = TextDecorations.Strikethrough, Foreground = Brushes.IndianRed });
            text.Inlines.Add("  →  ");
            text.Inlines.Add(new Run(now) { Foreground = Brushes.SeaGreen, FontWeight = FontWeights.SemiBold });
        }
        return text;
    }

    // ---- Settings ----

    void LoadSettings()
    {
        updating = true;
        var s = app.Store.Settings;
        AutoStartBox.IsChecked = Autostart.Enabled;
        StartupProfileBox.ItemsSource = app.Store.Profiles.Select(p => p.Name).Prepend("Don't apply").ToList();
        StartupProfileBox.SelectedIndex = Math.Max(0, app.Store.Profiles.FindIndex(p => p.Name == s.StartupProfile) + 1);
        TrayClickBox.SelectedIndex = (int)s.TrayClick;
        HotkeysBox.IsChecked = s.Hotkeys;
        ConfirmBox.IsChecked = s.ConfirmChanges;
        RevertBox.SelectedIndex = Math.Max(0, Array.IndexOf(RevertChoices, s.RevertSeconds));
        MissingBox.SelectedIndex = (int)s.MissingDevice;
        FilePathText.Text = app.Store.FilePath;
        updating = false;
    }

    void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (updating) return;
        var s = app.Store.Settings;
        Autostart.Enabled = AutoStartBox.IsChecked == true;
        s.StartupProfile = StartupProfileBox.SelectedIndex > 0 ? app.Store.Profiles[StartupProfileBox.SelectedIndex - 1].Name : null;
        s.TrayClick = (TrayClick)Math.Max(0, TrayClickBox.SelectedIndex);
        s.Hotkeys = HotkeysBox.IsChecked == true;
        s.ConfirmChanges = ConfirmBox.IsChecked == true;
        s.RevertSeconds = RevertChoices[Math.Max(0, RevertBox.SelectedIndex)];
        s.MissingDevice = (MissingAction)Math.Max(0, MissingBox.SelectedIndex);
        app.Save();
    }

    void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Store.Folder);
        Process.Start("explorer.exe", Store.Folder);
    }

    void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { FileName = "DeskScene-profiles.json", Filter = "JSON files|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        app.Store.Save();
        File.Copy(app.Store.FilePath, dialog.FileName, overwrite: true);
    }

    void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "JSON files|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var count = app.Store.Import(dialog.FileName);
            app.Save();
            PromptWindow.Ask("Import complete", $"Imported {count} profile(s). Profiles with the same name were replaced.", ["OK"]);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            PromptWindow.Ask("Import failed", ex.Message, ["OK"]);
        }
    }
}
