using System.Windows;

namespace DeskScene;

/// <summary>Asks for a profile name. With a snapshot it is the "Save current setup" dialog; without, it is "Rename".</summary>
public partial class SaveDialog : Window
{
    readonly IEnumerable<string> takenNames;

    public string ProfileName => NameBox.Text.Trim();
    public string? Hotkey => string.IsNullOrEmpty(HotkeyInput.Text) ? null : HotkeyInput.Text;

    public SaveDialog(Profile? snapshot, string name, IEnumerable<string> takenNames)
    {
        InitializeComponent();
        this.takenNames = takenNames;
        NameBox.Text = name;
        Title = Heading.Text = snapshot == null ? "Rename profile" : "Save current setup as profile";
        OkButton.Content = snapshot == null ? "Rename" : "Save";
        if (snapshot == null)
        {
            Preview.Visibility = Visibility.Collapsed;
        }
        else
        {
            Thumb.Monitors = snapshot.Display.Monitors;
            var audio = snapshot.Audio;
            Recorded.Text = string.Join("\n", snapshot.Display.Monitors.Select(m => $"• {m.Name} — {m.Describe()}")
                .Append($"• Audio out: {audio.Output?.Name ?? "none"}")
                .Append($"• Communications: {audio.Communications?.Name ?? "none"}")
                .Append($"• Audio in: {audio.Input?.Name ?? "none"}")
                .Append(audio.Volume is int v ? $"• Volume {v}%" : "")
                .Where(line => line != ""));
        }
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    void OnOk(object sender, RoutedEventArgs e)
    {
        Error.Text = ProfileName == "" ? "Enter a name."
            : takenNames.Contains(ProfileName, StringComparer.OrdinalIgnoreCase) ? "A profile with this name already exists."
            : "";
        Error.Visibility = Error.Text == "" ? Visibility.Collapsed : Visibility.Visible;
        if (Error.Text == "") DialogResult = true;
    }
}
