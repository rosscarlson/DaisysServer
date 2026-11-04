using System.Windows;
using DaisysServer.Modules;

namespace DaisysServer.Modules.Processes;

/// <summary>
/// Daisy's App's Support → Process Analyzer's master database: every program its clients know (threat, whether it's
/// needed, expected use, publisher, the folders it belongs in), one database for the whole server. Clients send what
/// they've classified or changed (report), the server merges it (a reviewed entry beats an automatic one, then the
/// newer), numbers the changes with a version, and sends them to every client; a client joining gets every change since
/// the version it has (everything, for a new one). Unlike other modules it isn't sealed: programs' names and
/// classifications aren't users' data, and the server has to read them to merge them (asked for on 2026-11-04).
/// </summary>
[ServerModule("processes", "Processes", Description = "Daisy's App's Process Analyzer: the master database of known programs, shared by every client.", Icon = "")]
public sealed class ProcessesModule : IServerModule
{
    private IModuleHost host = null!;
    private ProcessDatabase db = null!;
    private ProcessesPage? page;

    public void Start(IModuleHost host)
    {
        this.host = host;
        db = new ProcessDatabase(host.Log);
    }

    public FrameworkElement? View => page ??= new ProcessesPage(db);

    public async Task ServeAsync(IModuleChannel channel, CancellationToken ct)
    {
        var peer = new ProcessDatabase.Peer(channel);
        try
        {
            while (await channel.ReadAsync(ct) is { } m) db.Handle(peer, m);
        }
        finally
        {
            db.Leave(peer);
            host.Log.Info($"{channel.User.Name} on {channel.Client} left");
        }
    }

    // one database for everyone: nothing in it is a user's
    public void UserRemoved(ServerUser user) { }

    public void Dispose() => db?.Dispose();
}
