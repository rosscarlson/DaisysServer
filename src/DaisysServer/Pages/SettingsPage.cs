using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DaisysServer.Modules;
using DaisysServer.Server;
using DaisysServer.Updates;

namespace DaisysServer.Pages;

/// <summary>
/// Settings: the server (its name and port), startup and the tray, updates (the version, checking at launch, Check for
/// updates), the modules (each switched on or off, from the next start), each module's own card (its SettingsView),
/// and its folders.
/// </summary>
internal sealed class SettingsPage : UserControl
{
    private readonly ServerHost server;
    private readonly TextBlock updateStatus = Ui.Secondary();

    public SettingsPage(ServerHost server, MainWindow window)
    {
        this.server = server;
        var s = server.Settings;

        // ---- the server
        var name = new TextBox { Width = 220, Text = s.Name };
        var port = new TextBox { Width = 90, Text = s.Port.ToString() };
        var serverResult = Ui.Secondary();
        serverResult.Margin = new Thickness(12, 0, 0, 0);
        serverResult.VerticalAlignment = VerticalAlignment.Center;
        var apply = Ui.Button("", "Apply", "Saves the name and port (listening again on the new port)", () =>
        {
            if (!int.TryParse(port.Text.Trim(), out int p) || p is < 1 or > 65535)
            {
                serverResult.Text = "A port is 1 to 65535.";
                return;
            }
            s.Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : Environment.MachineName;
            bool moved = p != s.Port;
            s.Port = p;
            s.Save();
            if (moved && server.Listening)
            {
                server.Stop();
                server.Start();
            }
            serverResult.Text = server.Problem ?? "✓ Saved";
        }, accent: true);
        var listen = Check("Listen when Daisy's Server starts", s.ListenOnStart, v => s.ListenOnStart = v);
        var serverCard = new StackPanel();
        serverCard.Children.Add(Ui.Header("Server"));
        serverCard.Children.Add(Ui.Row(Ui.Label("Name clients see"), name));
        serverCard.Children.Add(Ui.Row(Ui.Label("Port (TCP)"), port));
        serverCard.Children.Add(Ui.Row(apply, serverResult));
        serverCard.Children.Add(listen);
        var firewall = Ui.Secondary("The installer let Daisy's Server through Windows Firewall, on any port.");
        firewall.Margin = new Thickness(0, 6, 0, 0);
        serverCard.Children.Add(firewall);

        // ---- startup and tray
        var startup = new StackPanel();
        startup.Children.Add(Ui.Header("Startup and tray"));
        startup.Children.Add(Check("Start when I sign in to Windows (in the tray)", s.StartWithWindows, v => { s.StartWithWindows = v; DaisysServer.Startup.Apply(v); }));
        startup.Children.Add(Check("Keep running in the tray when the window is closed", s.KeepInTray, v => s.KeepInTray = v));

        // ---- updates
        var updates = new StackPanel();
        updates.Children.Add(Ui.Header("Updates"));
        updates.Children.Add(new TextBlock { Text = $"Daisy's Server {AppInfo.VersionText}", Margin = new Thickness(0, 0, 0, 8) });
        updates.Children.Add(Check("Check for updates when Daisy's Server starts", s.CheckUpdatesOnLaunch, v => s.CheckUpdatesOnLaunch = v));
        var check = Ui.Button("", "Check for updates", null, () => _ = window.CheckForUpdatesAsync(quiet: false), accent: true);
        var notes = Ui.Button("", "Release notes", UpdateService.ReleasesUrl, () => UpdateService.Open(UpdateService.ReleasesUrl));
        notes.Margin = new Thickness(8, 0, 0, 0);
        updateStatus.Margin = new Thickness(12, 0, 0, 0);
        updateStatus.VerticalAlignment = VerticalAlignment.Center;
        updates.Children.Add(Ui.Row(check, notes, updateStatus));

        // ---- modules
        var modules = new StackPanel();
        modules.Children.Add(Ui.Header("Modules"));
        var restart = Ui.Button("", "Restart now", "Switches the modules on or off", () => App.Current.Restart(), accent: true);
        restart.Visibility = Visibility.Collapsed;
        if (server.Catalog.Modules.Count == 0) modules.Children.Add(Ui.Secondary("No modules found in " + ModuleCatalog.Folder));
        foreach (var m in server.Catalog.Modules)
        {
            var box = new CheckBox { IsChecked = m.Enabled, Margin = new Thickness(0, 0, 0, 2) };
            box.Content = new TextBlock { Text = $"{m.Name}  {m.Version}", FontWeight = FontWeights.SemiBold };
            box.Click += (_, _) =>
            {
                s.DisabledModules.RemoveAll(x => x.Equals(m.Id, StringComparison.OrdinalIgnoreCase));
                if (box.IsChecked != true) s.DisabledModules.Add(m.Id);
                s.Save();
                restart.Visibility = server.Catalog.Modules.Any(x => x.Enabled == s.DisabledModules.Contains(x.Id, StringComparer.OrdinalIgnoreCase))
                    ? Visibility.Visible : Visibility.Collapsed;
            };
            modules.Children.Add(box);
            var about = Ui.Secondary(m.Problem != null ? $"Couldn't start: {m.Problem}" : m.Description);
            about.Margin = new Thickness(22, 0, 0, 10);
            modules.Children.Add(about);
        }
        var changes = Ui.Secondary("A module switched off isn't loaded at all, and clients asking for it are told so. Changes apply after a restart.");
        changes.Margin = new Thickness(0, 0, 0, 8);
        modules.Children.Add(changes);
        modules.Children.Add(restart);

        // ---- folders
        var files = new StackPanel();
        files.Children.Add(Ui.Header("Folders"));
        var settingsFolder = Folder("Settings, users and tokens", JsonFile.SettingsFolder);
        var dataFolder = Folder("Users' data", JsonFile.DataFolder);
        var logs = Folder("Logs", ModuleLog.Folder);
        dataFolder.Margin = logs.Margin = new Thickness(8, 0, 0, 0);
        files.Children.Add(Ui.Row(settingsFolder, dataFolder, logs));

        // ---- each module's own settings (Files: the shared drives)
        var cards = new List<UIElement> { Ui.Card(serverCard), Ui.Card(startup), Ui.Card(updates), Ui.Card(modules) };
        foreach (var m in server.Catalog.Modules)
        {
            try
            {
                if (m.Instance?.SettingsView is { } view) cards.Add(Ui.Card(view));
            }
            catch (Exception ex) { ModuleLog.Server.Warn($"Module {m.Name}: its settings couldn't be shown", ex); }
        }
        cards.Add(Ui.Card(files));
        Content = Ui.Page(cards.ToArray());
    }

    private CheckBox Check(string text, bool value, Action<bool> set)
    {
        var c = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 0, 0, 8) };
        c.Click += (_, _) =>
        {
            set(c.IsChecked == true);
            server.Settings.Save();
        };
        return c;
    }

    private static Button Folder(string text, string path) => Ui.Button("", text, path, () =>
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    });

    public void UpdateStatus(string text) => updateStatus.Text = text;
}
