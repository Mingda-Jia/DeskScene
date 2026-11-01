using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DeskScene;

/// <summary>Draws a profile's display arrangement like Settings › Display. Duplicated displays share one rectangle ("1 | 3").</summary>
public sealed class LayoutView : FrameworkElement
{
    public static readonly DependencyProperty MonitorsProperty = DependencyProperty.Register(
        nameof(Monitors), typeof(IEnumerable<Monitor>), typeof(LayoutView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable<Monitor>? Monitors
    {
        get => (IEnumerable<Monitor>?)GetValue(MonitorsProperty);
        set => SetValue(MonitorsProperty, value);
    }

    /// <summary>Large mode: thicker outlines and monitor names under the numbers.</summary>
    public bool ShowNames { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        var groups = (Monitors ?? []).Where(m => m.Active)
            .Select((m, i) => (Monitor: m, Number: i + 1))
            .GroupBy(x => (x.Monitor.X, x.Monitor.Y, x.Monitor.Width, x.Monitor.Height))
            .ToList();
        if (groups.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

        double left = groups.Min(g => g.Key.X), top = groups.Min(g => g.Key.Y);
        double width = groups.Max(g => g.Key.X + g.Key.Width) - left, height = groups.Max(g => g.Key.Y + g.Key.Height) - top;
        if (width <= 0 || height <= 0) return;
        var scale = Math.Min(ActualWidth / width, ActualHeight / height) * (ShowNames ? 0.85 : 1);
        double offsetX = (ActualWidth - width * scale) / 2, offsetY = (ActualHeight - height * scale) / 2;

        var accent = SystemColors.AccentColorBrush;
        var accentFill = new SolidColorBrush(Color.FromArgb(40, SystemColors.AccentColor.R, SystemColors.AccentColor.G, SystemColors.AccentColor.B));
        var foreground = TextElement.GetForeground(this) ?? Brushes.Gray;
        var gap = ShowNames ? 3 : 1;

        foreach (var g in groups)
        {
            var rect = new Rect(offsetX + (g.Key.X - left) * scale + gap, offsetY + (g.Key.Y - top) * scale + gap,
                Math.Max(1, g.Key.Width * scale - 2 * gap), Math.Max(1, g.Key.Height * scale - 2 * gap));
            var primary = g.Any(x => x.Monitor.Primary);
            dc.DrawRoundedRectangle(primary ? accentFill : null, new Pen(primary ? accent : Brushes.Gray, ShowNames ? 2 : 1.2), rect, 3, 3);

            var label = string.Join(" | ", g.Select(x => x.Number));
            if (ShowNames) label += "\n" + string.Join(" = ", g.Select(x => x.Monitor.Name));
            var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"),
                ShowNames ? 13 : 8, primary ? accent : foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                TextAlignment = TextAlignment.Center,
                MaxTextWidth = Math.Max(1, rect.Width - 4),
                MaxLineCount = 3,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            if (text.Height <= rect.Height) dc.DrawText(text, new Point(rect.X + 2, rect.Y + (rect.Height - text.Height) / 2));
        }
    }
}
