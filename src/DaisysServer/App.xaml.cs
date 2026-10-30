using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using DaisysServer.Modules;
using DaisysServer.Server;

namespace DaisysServer;

/// <summary>The program's name and version.</summary>
public static class AppInfo
{
    public static readonly Version Version = Updates.UpdateService.ParseVersion(
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) ?? new Version(0, 0, 0);
    public static string VersionText => Updates.UpdateService.Display(Version);
    public static string ExePath => Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "DaisysServer.exe");
}

/// <summary>
/// Starts the server: one copy at a time (starting it again shows the running one), the server and its modules, the
/// window (or only the tray, with --tray), and the update check. --exit asks a running copy to close (the installer
/// uses it); --restart waits for the running copy to go first.
/// </summary>
public partial class App : Application
{
    // (a copy with its own DAISYSSERVER_HOME runs beside the real one)
    private static readonly string Instance = JsonFile.Home == null ? "" : "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonFile.Home.ToLowerInvariant())))[..8];
    private static readonly string MutexName = @"Local\DaisysServer-Running" + Instance;
    private static readonly string ShowEventName = @"Local\DaisysServer-Show" + Instance;
    private static readonly string ExitEventName = @"Local\DaisysServer-Exit" + Instance;

    private Mutex? mutex;
    public static ServerHost Server { get; private set; } = null!;
    public static MainWindow? Window { get; private set; }
    private Tray? tray;
    private bool exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args.Select(a => a.ToLowerInvariant()).ToList();
        if (args.Contains("--exit"))
        {
            Signal(ExitEventName);
            Shutdown();
            return;
        }
        mutex = new Mutex(true, MutexName, out bool first);
        if (!first && args.Contains("--restart"))
        {
            try { first = mutex.WaitOne(TimeSpan.FromSeconds(20)); }
            catch (AbandonedMutexException) { first = true; }
        }
        if (!first)
        {
            Signal(ShowEventName);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            ModuleLog.Server.Error("Unhandled", ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => ModuleLog.Server.Error("Unhandled (background)", ex.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ex) => ex.SetObserved();

        ModuleLog.Tidy();
        ModuleLog.Server.Info($"Daisy's Server {AppInfo.VersionText} starting");
        Server = new ServerHost();
        Server.LoadModules();
        if (Server.Settings.ListenOnStart) Server.Start();
        if (JsonFile.Home == null) DaisysServer.Startup.Apply(Server.Settings.StartWithWindows);

        Window = new MainWindow(Server);
        tray = new Tray(Server, ShowWindow, ExitApp);
        if (!args.Contains("--tray") || !Server.Settings.KeepInTray) ShowWindow();

        Listen(ShowEventName, ShowWindow);
        Listen(ExitEventName, ExitApp);

        if (Server.Settings.CheckUpdatesOnLaunch)
            Dispatcher.BeginInvoke(() => _ = Window.CheckForUpdatesAsync(quiet: true), DispatcherPriority.ApplicationIdle);
    }

    private static void Signal(string name)
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(name);
            ev.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    private void Listen(string name, Action action)
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        var t = new Thread(() =>
        {
            while (true)
            {
                ev.WaitOne();
                Dispatcher.BeginInvoke(action);
            }
        }) { IsBackground = true, Name = name };
        t.Start();
    }

    public void ShowWindow()
    {
        if (Window == null) return;
        if (!Window.IsVisible) Window.Show();
        if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
        Window.Activate();
    }

    /// <summary>Closing the window: to the tray (if that's on), or out.</summary>
    public bool ClosingWindow()
    {
        if (exiting) return true;
        if (Server.Settings.KeepInTray)
        {
            tray?.FirstHide();
            return false;
        }
        ExitApp();
        return true;
    }

    public void ExitApp()
    {
        if (exiting) return;
        exiting = true;
        ModuleLog.Server.Info("Exiting");
        Window?.SaveBounds();
        Window?.Close();
        tray?.Dispose();
        Server.Dispose();
        try { mutex?.ReleaseMutex(); } catch { }
        Shutdown();
    }

    /// <summary>Starts again (modules switched on or off).</summary>
    public void Restart()
    {
        Process.Start(new ProcessStartInfo(AppInfo.ExePath, "--restart") { UseShellExecute = false });
        ExitApp();
    }

    public static new App Current => (App)Application.Current;
}
