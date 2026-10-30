using System.IO;
using System.Net;
using System.Net.Sockets;
using DaisysServer.Modules;

namespace DaisysServer.Server;

/// <summary>
/// The running server: its settings, users, tokens and modules, and the listener (TCP, on every address, at the port
/// in Settings) with its connections (<see cref="Session"/>).
/// </summary>
public sealed class ServerHost : IDisposable
{
    public ServerSettings Settings { get; }
    public UserStore Users { get; }
    public TokenStore Tokens { get; }
    public ModuleCatalog Catalog { get; } = new();

    private readonly object gate = new();
    private readonly List<Session> sessions = new();
    private TcpListener? listener;
    private CancellationTokenSource? stop;

    public bool Listening => listener != null;
    public int ListeningPort { get; private set; }
    /// <summary>Why it isn't listening (the port taken, say), or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>Listening started or stopped, or a connection came, signed in or went (any thread).</summary>
    public event Action? Changed;

    public ServerHost()
    {
        Settings = ServerSettings.Load();
        Users = new UserStore(Convert.FromBase64String(Settings.Secret));
        Tokens = new TokenStore();
        Tokens.Revoked += id =>
        {
            foreach (var s in Sessions().Where(s => s.TokenId == id)) s.Close("The token this connection used was revoked.");
        };
        Users.Removed += u =>
        {
            foreach (var s in Sessions().Where(s => s.User?.Id == u.Id)) s.Close("Your account was changed on the server: sign in again.");
            foreach (var m in Catalog.Modules)
            {
                try { m.Instance?.UserRemoved(u); }
                catch (Exception ex) { ModuleLog.Server.Warn($"Module {m.Name}: forgetting {u.Name}", ex); }
            }
            try { Directory.Delete(Path.Combine(JsonFile.DataFolder, "users", u.Id), true); } catch (DirectoryNotFoundException) { }
            catch (Exception ex) { ModuleLog.Server.Warn($"{u.Name}'s data couldn't all be deleted", ex); }
        };
        Users.Changed += RaiseChanged;
    }

    public void LoadModules() => Catalog.Load(Settings, m => new ModuleHost(this, m));

    public List<Session> Sessions() { lock (gate) return sessions.ToList(); }

    public void RaiseChanged() => Changed?.Invoke();

    public void Start()
    {
        if (listener != null) return;
        try
        {
            var l = new TcpListener(IPAddress.IPv6Any, Settings.Port);
            l.Server.DualMode = true;
            l.Start();
            listener = l;
            ListeningPort = Settings.Port;
            Problem = null;
            stop = new CancellationTokenSource();
            _ = AcceptLoopAsync(l, stop.Token);
            ModuleLog.Server.Info($"Listening on port {Settings.Port}");
        }
        catch (SocketException ex)
        {
            Problem = ex.SocketErrorCode == SocketError.AddressAlreadyInUse ? $"Port {Settings.Port} is in use by another program." : ex.Message;
            ModuleLog.Server.Error($"Couldn't listen on port {Settings.Port}: {ex.Message}");
        }
        RaiseChanged();
    }

    public void Stop()
    {
        if (listener == null) return;
        stop?.Cancel();
        listener.Stop();
        listener = null;
        foreach (var s in Sessions()) s.Close("The server stopped.");
        ModuleLog.Server.Info("Stopped listening");
        RaiseChanged();
    }

    private async Task AcceptLoopAsync(TcpListener l, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await l.AcceptTcpClientAsync(ct); }
            catch { return; }
            var s = new Session(this, tcp);
            lock (gate) sessions.Add(s);
            _ = Task.Run(async () =>
            {
                try { await s.RunAsync(); }
                finally
                {
                    lock (gate) sessions.Remove(s);
                    RaiseChanged();
                }
            });
        }
    }

    public void Dispose()
    {
        Stop();
        Catalog.Dispose();
    }

    /// <summary>What one module is given: its log, its folder per user, the users.</summary>
    private sealed class ModuleHost(ServerHost server, LoadedModule module) : IModuleHost
    {
        public ModuleLog Log { get; } = ModuleLog.For(module.Name);

        public string FolderFor(ServerUser user)
        {
            string dir = Path.Combine(JsonFile.DataFolder, "users", user.Id, module.Id);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public IReadOnlyList<ServerUser> Users => server.Users.All().Select(u => u.ToServerUser()).ToList();

        public event Action? Changed
        {
            add => server.Changed += value;
            remove => server.Changed -= value;
        }
    }
}
