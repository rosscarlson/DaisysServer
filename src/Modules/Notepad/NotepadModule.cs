using System.Text.Json.Nodes;
using System.Windows;
using DaisysServer.Modules;

namespace DaisysServer.Modules.Notepad;

/// <summary>
/// Daisy's App's Tools → Notepad, hosted here: each user's notes (tabs) shared by every client they sign in on. The
/// notes are sealed on the clients with the user's key, so this module never reads them: it numbers each note's
/// changes in order (turning back one made on an older version, as a Daisy's App host does), keeps them, and sends
/// them to the user's other clients, who merge them (operational transformation) themselves. Each note is kept as a
/// snapshot (sealed, sent by a client every 50 changes) and the changes since. One <see cref="Hub"/> per user.
/// </summary>
[ServerModule("notepad", "Notepad", Description = "Daisy's App's Notepad: each user's notes, in named tabs, shared live between their clients and end-to-end encrypted.", Icon = "")]
public sealed class NotepadModule : IServerModule
{
    private IModuleHost host = null!;
    private readonly Dictionary<string, Hub> hubs = new();
    private NotepadPage? page;

    public void Start(IModuleHost host)
    {
        this.host = host;
    }

    public FrameworkElement? View => page ??= new NotepadPage(this, host);

    internal Hub HubFor(ServerUser user)
    {
        lock (hubs)
        {
            if (!hubs.TryGetValue(user.Id, out var hub))
                hubs[user.Id] = hub = new Hub(user, host.FolderFor(user), host.Log);
            return hub;
        }
    }

    /// <summary>Every user's notes as kept (for the page): loaded from disk for users not connected.</summary>
    internal IEnumerable<(ServerUser User, Hub.Summary Summary)> Summaries()
    {
        foreach (var u in host.Users) yield return (u, HubFor(u).Summarise());
    }

    public async Task ServeAsync(IModuleChannel channel, CancellationToken ct)
    {
        var hub = HubFor(channel.User);
        var peer = new Hub.Peer(channel);
        host.Log.Info($"{channel.User.Name} on {channel.Client} joined");
        try
        {
            while (await channel.ReadAsync(ct) is { } m) hub.Handle(peer, m);
        }
        finally
        {
            hub.Leave(peer);
            host.Log.Info($"{channel.User.Name} on {channel.Client} left");
        }
    }

    public void UserRemoved(ServerUser user)
    {
        Hub? hub;
        lock (hubs)
        {
            hubs.Remove(user.Id, out hub);
        }
        hub?.Forget();
    }

    public void Dispose()
    {
        lock (hubs)
        {
            foreach (var h in hubs.Values) h.Flush();
            hubs.Clear();
        }
    }
}
