using System.IO;
using System.Windows;
using System.Windows.Controls;
using DaisysServer.Modules;

namespace DaisysServer.Modules.Files;

/// <summary>Its card in Settings: a box for each drive (this PC's, and any shared that aren't connected now), and
/// basic users too. Saved as they change; read again each time the page shows (a USB drive plugged in).</summary>
internal sealed class FilesSettingsView : StackPanel
{
    private readonly FilesSettings settings;
    private readonly StackPanel drives = new();

    public FilesSettingsView(FilesSettings settings)
    {
        this.settings = settings;
        Children.Add(Ui.Header("Shared drives (Files)"));
        Children.Add(drives);
        var basic = new CheckBox
        {
            Content = "Basic users too", IsChecked = settings.BasicUsers, Margin = new Thickness(0, 6, 0, 0),
            ToolTip = "Otherwise only admins can browse them",
        };
        basic.Click += (_, _) =>
        {
            settings.BasicUsers = basic.IsChecked == true;
            settings.Save();
        };
        Children.Add(basic);
        IsVisibleChanged += (_, _) => { if (IsVisible) Fill(); };
        Fill();
    }

    private async void Fill()
    {
        // (off the UI thread: a network drive that's gone can take a while to say so)
        var found = await Task.Run(() =>
        {
            var roots = new List<string>();
            try { roots.AddRange(DriveInfo.GetDrives().Select(d => d.Name)); } catch { }
            foreach (var s in settings.Shared()) if (!roots.Contains(s, StringComparer.OrdinalIgnoreCase)) roots.Add(s);
            roots.Sort(StringComparer.OrdinalIgnoreCase);
            return roots.Select(r => (Root: r, Drive: FilesModule.Drive(r))).ToList();
        });
        drives.Children.Clear();
        foreach (var (root, d) in found)
        {
            string text = (string?)d["name"] ?? root;
            if (d["total"] is { } total && d["free"] is { } free) text += $"   {Ui.Size((long)free!)} free of {Ui.Size((long)total)}";
            var box = new CheckBox { Content = text, IsChecked = settings.IsShared(root), Margin = new Thickness(0, 0, 0, 6) };
            box.Click += (_, _) => settings.Share(root, box.IsChecked == true);
            drives.Children.Add(box);
        }
    }
}
