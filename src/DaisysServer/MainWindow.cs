using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DaisysServer.Modules;
using DaisysServer.Pages;
using DaisysServer.Server;
using DaisysServer.Updates;

namespace DaisysServer;

/// <summary>
/// The window: the notification bar (updates, messages), the tabs (Status, Users, Tokens, a tab per module with a
/// page, Log and Settings, always last) and the page showing. Closing it keeps the server running in the tray.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly ServerHost server;
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 0) };
    private readonly ContentControl page = new();
    private readonly Border banner = new() { Visibility = Visibility.Collapsed, Padding = new Thickness(14, 8, 10, 8) };
    private readonly TextBlock bannerText = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel bannerButtons = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, (RadioButton Tab, Func<FrameworkElement> Make)> pages = new();
    private readonly Dictionary<string, FrameworkElement> made = new();
    private readonly SettingsPage settingsPage;
    private bool checking;

    public MainWindow(ServerHost server)
    {
        this.server = server;
        Title = "Daisy's Server";
        Width = 1000;
        Height = 700;
        MinWidth = 640;
        MinHeight = 420;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/DaisysServer.ico"));
        SetResourceReference(BackgroundProperty, "WindowBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        if (server.Settings.Bounds is [var l, var t, var w, var h] && w > 300 && h > 200
            && l + w > SystemParameters.VirtualScreenLeft + 50 && l < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50
            && t > SystemParameters.VirtualScreenTop - 20 && t < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            (Left, Top, Width, Height) = (l, t, w, h);
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        banner.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        banner.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        banner.BorderThickness = new Thickness(0, 0, 0, 2);
        var bannerGrid = new DockPanel();
        DockPanel.SetDock(bannerButtons, Dock.Right);
        bannerGrid.Children.Add(bannerButtons);
        bannerGrid.Children.Add(bannerText);
        banner.Child = bannerGrid;

        settingsPage = new SettingsPage(server, this);
        Add("status", "Status", () => new StatusPage(server, Go));
        Add("users", "Users", () => new UsersPage(server));
        Add("tokens", "Tokens", () => new TokensPage(server));
        foreach (var m in server.Catalog.Modules)
            if (m.Instance is { } instance && instance.View != null) Add("module:" + m.Id, m.Name, () => instance.View!);
        Add("log", "Log", () => new LogPage());
        Add("settings", "Settings", () => settingsPage);

        var root = new DockPanel();
        DockPanel.SetDock(banner, Dock.Top);
        root.Children.Add(banner);
        var strip = new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        DockPanel.SetDock(strip, Dock.Top);
        root.Children.Add(strip);
        var line = new Border { Height = 1 };
        line.SetResourceReference(Border.BackgroundProperty, "CardBorderBrush");
        DockPanel.SetDock(line, Dock.Top);
        root.Children.Add(line);
        root.Children.Add(page);
        Content = root;

        Go(server.Settings.LastPage is { } last && pages.ContainsKey(last) ? last : "status");
        SourceInitialized += (_, _) => DarkTitleBar();
        Closing += (_, e) =>
        {
            SaveBounds();
            if (!App.Current.ClosingWindow()) { e.Cancel = true; Hide(); }
        };
    }

    private void Add(string key, string title, Func<FrameworkElement> make)
    {
        var tab = new RadioButton { Content = title, Padding = new Thickness(14, 5, 14, 7), MinHeight = 32, Focusable = false, GroupName = "pages" };
        tab.SetResourceReference(StyleProperty, "TabButton");
        tab.Checked += (_, _) => Go(key);
        tabs.Children.Add(tab);
        pages[key] = (tab, make);
    }

    /// <summary>Shows a page (made the first time it's shown).</summary>
    public void Go(string key)
    {
        if (!pages.TryGetValue(key, out var p)) return;
        if (!made.TryGetValue(key, out var view)) made[key] = view = p.Make();
        page.Content = view;
        p.Tab.IsChecked = true;
        if (server.Settings.LastPage != key)
        {
            server.Settings.LastPage = key;
            server.Settings.Save();
        }
    }

    public void SaveBounds()
    {
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (b.Width > 0) server.Settings.Bounds = [b.Left, b.Top, b.Width, b.Height];
        server.Settings.Save();
    }

    // ---- the notification bar

    /// <summary>A message across the top; buttons after it; null closes it.</summary>
    public void Notify(string? text, bool problem = false, params (string Text, Action Click, bool Accent)[] buttons)
    {
        bannerButtons.Children.Clear();
        if (text == null) { banner.Visibility = Visibility.Collapsed; return; }
        bannerText.Text = text;
        banner.SetResourceReference(Border.BorderBrushProperty, problem ? "DangerBrush" : "AccentBrush");
        foreach (var (t, click, accent) in buttons)
        {
            var b = Ui.Button("", t, null, click, accent);
            b.Margin = new Thickness(8, 0, 0, 0);
            bannerButtons.Children.Add(b);
        }
        var close = new Button { Content = "✕", Margin = new Thickness(8, 0, 0, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = "Close" };
        close.Click += (_, _) => Notify(null);
        bannerButtons.Children.Add(close);
        banner.Visibility = Visibility.Visible;
    }

    /// <summary>Checks GitHub for a newer version: offered in the bar (nothing installs until Install update is
    /// clicked). <paramref name="quiet"/> (at launch): says nothing when there's none, or when it can't check.</summary>
    public async Task CheckForUpdatesAsync(bool quiet)
    {
        if (checking) return;
        checking = true;
        settingsPage.UpdateStatus("Checking…");
        try
        {
            var update = await UpdateService.CheckAsync();
            if (update == null)
            {
                settingsPage.UpdateStatus($"Up to date ({AppInfo.VersionText})");
                if (!quiet) Notify($"Daisy's Server {AppInfo.VersionText} is the latest version.");
                return;
            }
            string v = UpdateService.Display(update.Version);
            settingsPage.UpdateStatus($"Version {v} is available");
            ModuleLog.Server.Info($"Update {v} available");
            Notify($"Daisy's Server {v} is available (you have {AppInfo.VersionText}).", false,
                ("What's new", () => UpdateService.Open(update.ReleaseUrl), false),
                ("Install update", () => _ = InstallAsync(update), true));
        }
        catch (Exception ex)
        {
            ModuleLog.Server.Warn("Update check failed", ex);
            settingsPage.UpdateStatus("Couldn't check: " + ex.Message);
            if (!quiet) Notify("Couldn't check for updates: " + ex.Message, true);
        }
        finally { checking = false; }
    }

    private async Task InstallAsync(UpdateInfo update)
    {
        string v = UpdateService.Display(update.Version);
        try
        {
            Notify($"Downloading {v}…");
            var path = await UpdateService.DownloadAsync(update, f => Dispatcher.BeginInvoke(() => bannerText.Text = $"Downloading {v}… {f:P0}"));
            Notify($"Installing {v}: Daisy's Server restarts when it's done.");
            ModuleLog.Server.Info($"Installing {v} from {path}");
            UpdateService.LaunchInstaller(path);
            await Task.Delay(500);
            App.Current.ExitApp();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Notify("The update was cancelled (it needs admin approval).", true);
        }
        catch (Exception ex)
        {
            ModuleLog.Server.Error("Update failed", ex);
            Notify("The update failed: " + ex.Message, true, ("Try again", () => _ = InstallAsync(update), true));
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void DarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int dark = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0) DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
    }
}
