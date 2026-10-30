using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DaisysServer.Modules;

namespace DaisysServer.Pages;

/// <summary>Log: the server's and its modules' lines as they're written (the last thousand), and its folder.</summary>
internal sealed class LogPage : UserControl
{
    private readonly ListBox lines = new() { FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 12, BorderThickness = new Thickness(0) };

    public LogPage()
    {
        lines.SetResourceReference(BackgroundProperty, "CardBrush");
        lines.SetResourceReference(ForegroundProperty, "TextBrush");
        foreach (var e in ModuleLog.Recent) lines.Items.Add(Line(e));
        ModuleLog.Written += e => Dispatcher.BeginInvoke(() =>
        {
            lines.Items.Add(Line(e));
            while (lines.Items.Count > 1000) lines.Items.RemoveAt(0);
            if (IsVisible) lines.ScrollIntoView(lines.Items[^1]);
        });
        var open = Ui.Button("", "Open folder", ModuleLog.Folder, () =>
        {
            System.IO.Directory.CreateDirectory(ModuleLog.Folder);
            Process.Start(new ProcessStartInfo(ModuleLog.Folder) { UseShellExecute = true });
        });
        open.Margin = new Thickness(0, 0, 0, 8);
        var panel = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };
        DockPanel.SetDock(open, Dock.Top);
        panel.Children.Add(open);
        panel.Children.Add(Ui.Card(lines));
        Content = panel;
        IsVisibleChanged += (_, _) => { if (IsVisible && lines.Items.Count > 0) lines.ScrollIntoView(lines.Items[^1]); };
    }

    private static TextBlock Line(LogEntry e)
    {
        var t = new TextBlock { Text = $"{e.At:dd MMM HH:mm:ss}  [{e.Source}]  {e.Text}", TextWrapping = TextWrapping.NoWrap };
        if (e.Level == LogLevel.Error) t.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
        else if (e.Level == LogLevel.Warning) t.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        return t;
    }
}
