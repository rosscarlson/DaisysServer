using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace DaisysServer.Modules;

/// <summary>A dropdown of checkboxes, to pick several: it looks like a ComboBox and shows what's picked, joined.</summary>
public sealed class MultiPick : Grid
{
    private readonly ToggleButton toggle = new() { Focusable = false, ClickMode = ClickMode.Press };
    private readonly TextBlock shown = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 32, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Popup popup = new() { Placement = PlacementMode.Bottom, AllowsTransparency = true, StaysOpen = false, PopupAnimation = PopupAnimation.Fade };
    private readonly List<CheckBox> boxes;

    /// <summary>The picks changed (by a click, or <see cref="Selected"/> set).</summary>
    public event Action? Changed;

    public MultiPick(IEnumerable<string> options, string name)
    {
        MinHeight = 32;
        toggle.SetResourceReference(Control.TemplateProperty, "ComboToggleTemplate");
        System.Windows.Automation.AutomationProperties.SetName(toggle, name);
        Children.Add(toggle);
        Children.Add(shown);
        var list = new StackPanel();
        boxes = options.Select(o =>
        {
            var box = new CheckBox { Content = o, Margin = new Thickness(6, 4, 6, 4) };
            box.Checked += (_, _) => Picked();
            box.Unchecked += (_, _) => Picked();
            list.Children.Add(box);
            return box;
        }).ToList();
        var border = new Border { Child = list, Margin = new Thickness(0, 4, 0, 0), Padding = new Thickness(4), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        border.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        border.SetBinding(MinWidthProperty, new System.Windows.Data.Binding(nameof(ActualWidth)) { Source = this });
        popup.Child = border;
        popup.PlacementTarget = this;
        Children.Add(popup);
        toggle.Checked += (_, _) => popup.IsOpen = true;
        // a click on it while open closes it (not closes and opens again)
        popup.Opened += (_, _) => toggle.IsHitTestVisible = false;
        popup.Closed += (_, _) => { toggle.IsChecked = false; Dispatcher.BeginInvoke(() => toggle.IsHitTestVisible = true); };
        Picked();
    }

    private bool quiet;

    private void Picked()
    {
        var picked = Selected;
        shown.Text = picked.Count > 0 ? string.Join(", ", picked) : "None";
        shown.SetResourceReference(TextBlock.ForegroundProperty, picked.Count > 0 ? "TextBrush" : "TextSecondaryBrush");
        if (!quiet) Changed?.Invoke();
    }

    /// <summary>What's picked, in the options' order.</summary>
    public List<string> Selected
    {
        get => boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content).ToList();
        set
        {
            quiet = true;
            foreach (var b in boxes) b.IsChecked = value.Contains((string)b.Content, StringComparer.OrdinalIgnoreCase);
            quiet = false;
            Picked();
        }
    }
}
