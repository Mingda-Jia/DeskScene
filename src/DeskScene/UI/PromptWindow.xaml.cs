using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DeskScene;

public partial class PromptWindow : Window
{
    int result = -1;

    PromptWindow() => InitializeComponent();

    /// <summary>Shows a modal prompt and returns the index of the clicked button, or -1 if it was closed.
    /// With a countdown, <paramref name="timeoutResult"/> is returned when time runs out.</summary>
    public static int Ask(string title, object body, string[] buttons, int countdown = 0, int timeoutResult = -1)
    {
        var w = new PromptWindow();
        w.Heading.Text = title;
        w.Body.Content = body is string text ? new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } : body;
        for (int i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], MinWidth = 100, Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0), IsDefault = i == 0 };
            if (i == 0) button.SetResourceReference(StyleProperty, "AccentButtonStyle");
            button.Click += (_, _) => { w.result = index; w.Close(); };
            w.Buttons.Children.Add(button);
        }

        if (countdown > 0 && timeoutResult >= 0)
        {
            var left = countdown;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            void Update() => w.Countdown.Text = $"\"{buttons[timeoutResult]}\" in {left} seconds.";
            timer.Tick += (_, _) =>
            {
                if (--left > 0) { Update(); return; }
                w.result = timeoutResult;
                w.Close();
            };
            Update();
            w.Countdown.Visibility = Visibility.Visible;
            w.Closed += (_, _) => timer.Stop();
            timer.Start();
        }

        w.ShowDialog();
        return w.result;
    }
}
