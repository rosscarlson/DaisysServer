using System.Windows;
using DaisysServer.Server;
using Forms = System.Windows.Forms;

namespace DaisysServer;

/// <summary>The tray icon: its tooltip says whether it's listening and who's connected; double-click opens the window,
/// right-click offers Open, Start / Stop listening, Check for updates and Exit.</summary>
internal sealed class Tray : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly ServerHost server;
    private readonly Forms.ToolStripMenuItem listen;
    private bool told;

    public Tray(ServerHost server, Action show, Action exit)
    {
        this.server = server;
        icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(AppInfo.ExePath),
            Visible = true,
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Daisy's Server", null, (_, _) => show());
        listen = new Forms.ToolStripMenuItem("Stop listening", null, (_, _) =>
        {
            if (server.Listening) server.Stop(); else server.Start();
        });
        menu.Items.Add(listen);
        menu.Items.Add("Check for updates", null, (_, _) =>
        {
            show();
            _ = App.Window?.CheckForUpdatesAsync(quiet: false);
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());
        icon.ContextMenuStrip = menu;
        icon.DoubleClick += (_, _) => show();
        server.Changed += () => Application.Current?.Dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        int n = server.Sessions().Count(s => s.User != null);
        string text = server.Listening ? $"Daisy's Server: port {server.ListeningPort}, {n} connected" : "Daisy's Server: not listening";
        icon.Text = text.Length > 63 ? text[..63] : text;
        listen.Text = server.Listening ? "Stop listening" : "Start listening";
    }

    /// <summary>The window closed to the tray: said once, the first time.</summary>
    public void FirstHide()
    {
        if (told) return;
        told = true;
        icon.ShowBalloonTip(3000, "Daisy's Server", "Still running here in the tray. Right-click it to exit.", Forms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}

/// <summary>Start with Windows: HKCU's Run key, with --tray.</summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "DaisysServer";

    public static void Apply(bool on)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            if (on) key.SetValue(Name, $"\"{AppInfo.ExePath}\" --tray");
            else if (key.GetValue(Name) != null) key.DeleteValue(Name);
        }
        catch (Exception ex) { Modules.ModuleLog.Server.Warn("Start with Windows couldn't be set", ex); }
    }
}
