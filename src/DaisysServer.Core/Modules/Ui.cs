using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DaisysServer.Modules;

/// <summary>
/// The pieces every page is made of, in Daisy's App's look (the styles are the server's Themes\Controls.xaml): cards
/// with a header, secondary text, buttons with an icon, and the two-click Confirm? button for what can't be undone.
/// </summary>
public static class Ui
{
    public static Border Card(UIElement content)
    {
        var b = new Border { Child = content };
        b.SetResourceReference(FrameworkElement.StyleProperty, "Card");
        return b;
    }

    public static TextBlock Header(string text)
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(FrameworkElement.StyleProperty, "CardHeader");
        return t;
    }

    public static TextBlock Secondary(string text = "")
    {
        var t = new TextBlock { Text = text };
        t.SetResourceReference(FrameworkElement.StyleProperty, "SecondaryText");
        return t;
    }

    public static TextBlock Label(string text, double width = 130) =>
        new() { Text = text, Width = width, VerticalAlignment = VerticalAlignment.Center };

    public static TextBlock Glyph(string glyph, double size = 14)
    {
        var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        return t;
    }

    public static Button Button(string glyph, string text, string? tip, Action click, bool accent = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph.Length > 0) content.Children.Add(Glyph(glyph, 13));
        content.Children.Add(new TextBlock { Text = text, Margin = new Thickness(glyph.Length > 0 ? 8 : 0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var b = new Button { Content = content, ToolTip = tip, HorizontalAlignment = HorizontalAlignment.Left };
        if (accent) b.SetResourceReference(FrameworkElement.StyleProperty, "AccentButton");
        System.Windows.Automation.AutomationProperties.SetName(b, text);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A button that turns into a red <b>Confirm?</b> on the first click, does it on the second, and goes back
    /// after a few seconds left alone.</summary>
    public static Button Confirm(string glyph, string text, string? tip, Action action)
    {
        var b = Button(glyph, text, tip, () => { });
        var normal = b.Content;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        bool armed = false;
        void Reset()
        {
            timer.Stop();
            armed = false;
            b.Content = normal;
            b.ClearValue(FrameworkElement.StyleProperty);
        }
        timer.Tick += (_, _) => Reset();
        b.Click += (_, _) =>
        {
            if (!armed)
            {
                armed = true;
                b.Content = new TextBlock { Text = "Confirm?", FontWeight = FontWeights.SemiBold };
                b.SetResourceReference(FrameworkElement.StyleProperty, "DangerButton");
                timer.Start();
                return;
            }
            Reset();
            action();
        };
        return b;
    }

    public static StackPanel Row(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    /// <summary>A page: its cards one under another, scrolling.</summary>
    public static ScrollViewer Page(params UIElement[] cards)
    {
        var p = new StackPanel { Margin = new Thickness(16, 12, 16, 12) };
        foreach (var c in cards) p.Children.Add(c);
        return new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    public static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    /// <summary>A size in bytes, as KB or MB.</summary>
    public static string Size(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1 << 20 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / 1048576.0:0.#} MB";

    /// <summary>How long ago, briefly.</summary>
    public static string Ago(DateTime? utc)
    {
        if (utc is not { } t) return "never";
        var d = DateTime.UtcNow - t;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} h ago";
        return t.ToLocalTime().ToString("d MMM yyyy");
    }
}
