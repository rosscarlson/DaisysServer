using System.Text.Json.Nodes;
using System.Windows;
using DaisysServer.Modules;
using DaisysServer.Protocol;

namespace DaisysServer.Modules.Sessions;

/// <summary>
/// Daisy's App's SimEffects → Analysis → Share Session: racing sessions (telemetry logs, gzipped as the client sends
/// them) shared on the server, listed and downloaded by every user of it. Each question carries a rid its answer
/// repeats; what's shared, changed or deleted is pushed to every open channel. Not sealed: every user reads them, so
/// one store for the whole server. Admins find, remove and ban on its page.
/// </summary>
[ServerModule("sessions", "Sessions", Description = "Daisy's App's Share Session: racing sessions shared with every user.", Icon = "")]
public sealed class SessionsModule : IServerModule
{
    private IModuleHost host = null!;
    private SessionStore store = null!;
    private SessionsPage? page;

    public void Start(IModuleHost host)
    {
        this.host = host;
        store = new SessionStore(host.Log);
    }

    public FrameworkElement? View => page ??= new SessionsPage(store, () => host.Users);

    public async Task ServeAsync(IModuleChannel channel, CancellationToken ct)
    {
        var peer = new SessionStore.Peer(channel);
        store.Join(peer);
        try
        {
            while (await channel.ReadAsync(ct) is { } m) Answer(peer, m);
        }
        finally { store.Leave(peer); }
    }

    private void Answer(SessionStore.Peer peer, JsonObject m)
    {
        var c = peer.Channel;
        string op = ServerClient.Str(m, "op"), id = ServerClient.Str(m, "id");
        void Reply(JsonObject answer)
        {
            if (m["rid"] is JsonNode rid) answer["rid"] = rid.DeepClone();
            c.Post(answer);
        }
        void Error(string message) => Reply(new JsonObject { ["op"] = "error", ["message"] = message });
        long Long(string name) => m[name] is JsonValue v ? v.TryGetValue<long>(out var n) ? n : v.TryGetValue<double>(out var d) ? (long)d : 0 : 0;
        try
        {
            switch (op)
            {
                case "list":
                    Reply(new JsonObject { ["op"] = "list", ["sessions"] = store.List(c) });
                    return;
                case "share":
                    {
                        var (newId, problem) = store.Begin(peer, m["meta"] as JsonObject, Long("size"), ServerClient.Str(m, "sha256"));
                        if (problem != null) Error(problem);
                        else Reply(new JsonObject { ["op"] = "share", ["id"] = newId });
                        return;
                    }
                case "put":
                    {
                        var (received, problem) = store.Put(peer, id, Long("offset"), ServerClient.Str(m, "data"), (bool?)m["eof"] == true);
                        if (problem != null) Error(problem);
                        else Reply(new JsonObject { ["op"] = "put", ["id"] = id, ["received"] = received });
                        return;
                    }
                case "get":
                    {
                        long offset = Long("offset");
                        var (size, data, problem) = store.Get(c, id, offset, (int)Math.Min(Long("length"), SessionStore.MaxPiece));
                        if (problem != null) Error(problem);
                        else Reply(new JsonObject
                        {
                            ["op"] = "get", ["id"] = id, ["offset"] = offset, ["length"] = data.Length, ["size"] = size,
                            ["eof"] = offset + data.Length >= size, ["data"] = Convert.ToBase64String(data),
                        });
                        return;
                    }
                case "edit":
                    {
                        var (meta, problem) = store.Edit(c, m);
                        if (problem != null) Error(problem);
                        else Reply(new JsonObject { ["op"] = "edit", ["session"] = meta });
                        return;
                    }
                case "delete":
                    {
                        if (store.Delete(c, id) is { } problem) Error(problem);
                        else Reply(new JsonObject { ["op"] = "delete", ["id"] = id });
                        return;
                    }
                default:
                    Error($"This server can't do \"{op}\" yet: update it.");
                    return;
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            host.Log.Warn($"{c.User.Name} on {c.Client}: {op} failed", ex);
            Error("The server couldn't do that: " + ex.Message);
        }
    }

    // shared with everyone: a user's sessions stay when they're deleted or their password's reset (ban or remove them)
    public void UserRemoved(ServerUser user) { }

    public void Dispose() => store?.Dispose();
}
