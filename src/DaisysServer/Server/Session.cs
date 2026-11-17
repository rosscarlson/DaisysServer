using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using DaisysServer.Modules;
using DaisysServer.Protocol;

namespace DaisysServer.Server;

/// <summary>
/// One client's connection: the handshake (the server's key), its hello, its sign-in (or none: anonymous, for the
/// modules that allow it), then its questions and its channels to modules, each served by the module until either end
/// closes it. Everything it sends goes out in order through one queue. A connection that says nothing for two minutes is
/// dropped (clients ping every 30 seconds).
/// </summary>
public sealed class Session
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(2);

    private readonly ServerHost server;
    private readonly TcpClient tcp;
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<JsonNode> outbox = Channel.CreateUnbounded<JsonNode>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<int, ModuleChannel> channels = new();
    private Wire? wire;
    private int failures;
    private DateTime lastHeard = DateTime.UtcNow;

    public string Address { get; }
    public string Client { get; private set; } = "";
    public string App { get; private set; } = "";
    /// <summary>Who signed in; null: nobody (yet), so channels it opens are <see cref="Guest"/>'s.</summary>
    public ServerUser? User { get; private set; }
    /// <summary>Its anonymous user: "anon:" and a hash of the device id from its hello (one made up if it sent none).</summary>
    public ServerUser Guest { get; private set; } = new(ServerUser.AnonymousPrefix, "", false);
    public DateTime Since { get; } = DateTime.Now;
    /// <summary>The modules it has channels open to.</summary>
    public IReadOnlyList<string> Open => channels.Values.Select(c => c.Module).Distinct().ToList();

    public Session(ServerHost server, TcpClient tcp)
    {
        this.server = server;
        this.tcp = tcp;
        tcp.NoDelay = true;
        Address = tcp.Client.RemoteEndPoint is IPEndPoint ep ? $"{(ep.Address.IsIPv4MappedToIPv6 ? ep.Address.MapToIPv4() : ep.Address)}" : "?";
    }

    private ModuleLog Log => ModuleLog.Server;

    public async Task RunAsync()
    {
        var ct = stop.Token;
        try
        {
            using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                handshake.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    var w = await Wire.AcceptAsync(tcp.GetStream(), server.Key, handshake.Token);
                    wire = w;
                    var hello = await w.ReadAsync(handshake.Token);
                    if (hello == null || Str(hello, "op") != "hello") return;
                    Client = Clean(Str(hello, "client"), 60) is { Length: > 0 } c ? c : Address;
                    App = Clean(Str(hello, "app"), 40);
                    string device = Str(hello, "device") is { Length: >= 16 } d ? d : Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                    string anon = ServerUser.AnonymousPrefix + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(device)))[..16].ToLowerInvariant();
                    Guest = new ServerUser(anon, "", false, Client);
                }
                catch (Exception ex)
                {
                    Log.Info($"{Address}: handshake failed ({ex.Message})");
                    return;
                }
            }
            _ = SendLoopAsync(ct);
            _ = IdleLoopAsync(ct);
            Post(new JsonObject
            {
                ["op"] = "welcome", ["server"] = server.Settings.Name, ["version"] = AppInfo.VersionText, ["v"] = Wire.Version,
                ["modules"] = new JsonArray(server.Catalog.Modules.Where(m => m.Instance != null)
                    .Select(m => (JsonNode)new JsonObject { ["id"] = m.Id, ["name"] = m.Name, ["version"] = m.Version, ["anonymous"] = m.Instance!.Anonymous }).ToArray()),
            });
            Log.Info($"{Client} ({Address}) connected");
            server.RaiseChanged();
            while (await wire.ReadAsync(ct) is { } m)
            {
                lastHeard = DateTime.UtcNow;
                await HandleAsync(m);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or CryptographicException or SocketException or ObjectDisposedException or System.Text.Json.JsonException)
        {
            if (!stop.IsCancellationRequested) Log.Info($"{Describe()}: connection lost ({ex.Message})");
        }
        catch (Exception ex) { Log.Error($"{Describe()}: connection failed", ex); }
        finally
        {
            stop.Cancel();
            foreach (var c in channels.Values) c.End();
            channels.Clear();
            wire?.Dispose();
            tcp.Dispose();
            if (wire != null) Log.Info($"{Describe()} disconnected");
        }
    }

    private string Describe() => User != null ? $"{User.Name} on {Client}" : Client.Length > 0 ? Client : Address;

    private static string Str(JsonObject m, string name) => ServerClient.Str(m, name);
    private static string Clean(string s, int max) => new string(s.Where(c => !char.IsControl(c)).Take(max).ToArray()).Trim();

    public void Post(JsonNode m) => outbox.Writer.TryWrite(m);

    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var m in outbox.Reader.ReadAllAsync(ct)) await wire!.SendAsync(m, ct);
        }
        catch { stop.Cancel(); }
    }

    private async Task IdleLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
                if (DateTime.UtcNow - lastHeard > Idle)
                {
                    Log.Info($"{Describe()}: nothing heard for two minutes, dropped");
                    stop.Cancel();
                    tcp.Dispose();
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Ends the connection, telling the client why.</summary>
    public void Close(string why)
    {
        Post(new JsonObject { ["op"] = "bye", ["message"] = why });
        _ = Task.Delay(300).ContinueWith(_ =>
        {
            stop.Cancel();
            tcp.Dispose();
        });
    }

    private void Reply(JsonObject request, JsonObject answer)
    {
        if (request["rid"] is JsonNode rid) answer["rid"] = rid.DeepClone();
        Post(answer);
    }

    private void Error(JsonObject request, string message) => Reply(request, new JsonObject { ["op"] = "error", ["message"] = message });

    private async Task HandleAsync(JsonObject m)
    {
        // a channel's message
        if ((int?)m["ch"] is int ch && m["op"] == null)
        {
            if (channels.TryGetValue(ch, out var c) && m["msg"] is JsonObject msg) c.Deliver(msg);
            return;
        }
        string op = Str(m, "op");
        switch (op)
        {
            case "ping": Reply(m, new JsonObject { ["op"] = "pong" }); return;
            case "prelogin":
                {
                    var (salt, iter) = server.Users.Prelogin(Str(m, "user"));
                    Reply(m, new JsonObject { ["op"] = "prelogin", ["salt"] = salt, ["iter"] = iter });
                    return;
                }
            case "login":
                {
                    // (wrong passwords counted by address: anyone can get this far)
                    if (server.SignIns.Blocked(Address) is { } wait)
                    {
                        Error(m, $"Too many wrong passwords from your address: try again in {Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes))} minutes.");
                        return;
                    }
                    var u = server.Users.SignIn(Str(m, "user"), Str(m, "auth"));
                    if (u == null)
                    {
                        Log.Warn($"{Client} ({Address}): wrong user name or password for \"{Clean(Str(m, "user"), 40)}\"");
                        server.SignIns.Failed(Address);
                        await Task.Delay(1000);
                        Error(m, "Wrong user name or password.");
                        if (++failures >= 5) Close("Too many tries.");
                        return;
                    }
                    server.SignIns.Succeeded(Address);
                    User = u.ToServerUser();
                    Log.Info($"{Client} ({Address}) signed in as {u.Name}");
                    Reply(m, new JsonObject { ["op"] = "login", ["user"] = u.Name, ["admin"] = u.Admin, ["vault"] = u.Vault });
                    server.RaiseChanged();
                    return;
                }
            // (signed in or not: a module that doesn't allow anonymous ones says so)
            case "open": OpenChannel(m, User ?? Guest); return;
            case "close":
                if ((int?)m["ch"] is int closing && channels.TryRemove(closing, out var gone)) gone.End();
                return;
        }
        if (User is not { } user)
        {
            Error(m, "Sign in first.");
            return;
        }
        switch (op)
        {
            case "vault":
                Reply(m, new JsonObject { ["op"] = "vault", ["vault"] = server.Users.SetVault(user.Id, Str(m, "vault")) });
                return;
            case "password":
                if (server.Users.ChangePassword(user.Id, Str(m, "auth"), Str(m, "salt"), (int?)m["iter"] ?? Vault.Iterations, Str(m, "newAuth"), Str(m, "vault")))
                    Reply(m, new JsonObject { ["op"] = "password" });
                else Error(m, "The password you have now isn't right.");
                return;
        }
        // admins: the users
        if (!user.Admin)
        {
            Error(m, op.StartsWith("user", StringComparison.Ordinal) ? "Only an admin can do that." : $"This server can't do \"{op}\" yet: update it.");
            return;
        }
        string? problem;
        switch (op)
        {
            case "users":
                var list = new JsonArray();
                foreach (var u in server.Users.All())
                {
                    var profile = new JsonObject();
                    foreach (var f in UserStore.Fields) profile[f.Key] = u.Get(f.Key);
                    list.Add(new JsonObject { ["id"] = u.Id, ["name"] = u.Name, ["admin"] = u.Admin, ["created"] = u.Created, ["lastSignIn"] = u.LastSignIn, ["profile"] = profile, ["groups"] = new JsonArray(u.Groups.Select(g => (JsonNode)g).ToArray()) });
                }
                var fields = new JsonArray();
                foreach (var f in UserStore.Fields) fields.Add(new JsonObject { ["key"] = f.Key, ["label"] = f.Label, ["max"] = f.Max });
                Reply(m, new JsonObject { ["op"] = "users", ["users"] = list, ["fields"] = fields, ["groups"] = new JsonArray(UserStore.Groups.Select(g => (JsonNode)g).ToArray()) });
                return;
            case "useradd":
                problem = server.Users.Add(Str(m, "name"), Str(m, "password"), (bool?)m["admin"] == true, Profile(m), Groups(m));
                break;
            case "useredit":
                problem = server.Users.Edit(Str(m, "id"), m["name"] is JsonValue ? Str(m, "name") : null, Profile(m) ?? new());
                if (problem == null && Groups(m) is { } groups) problem = server.Users.SetGroups(Str(m, "id"), groups);
                break;
            case "userdelete": problem = Str(m, "id") == user.Id ? "You can't delete yourself." : server.Users.Delete(Str(m, "id")); break;
            case "useradmin": problem = server.Users.SetAdmin(Str(m, "id"), (bool?)m["admin"] == true); break;
            case "userpassword": problem = server.Users.ResetPassword(Str(m, "id"), Str(m, "password")); break;
            default: Error(m, $"This server can't do \"{op}\" yet: update it."); return;
        }
        if (problem != null) Error(m, problem);
        else Reply(m, new JsonObject { ["op"] = op });
    }

    /// <summary>The message's <c>profile</c> object as strings (useradd, useredit).</summary>
    private static Dictionary<string, string>? Profile(JsonObject m) =>
        m["profile"] is JsonObject p ? p.Where(kv => kv.Value is JsonValue).ToDictionary(kv => kv.Key, kv => Str(p, kv.Key)) : null;

    /// <summary>The message's <c>groups</c> array (useradd, useredit; since 0.2.3).</summary>
    private static List<string>? Groups(JsonObject m) =>
        m["groups"] is JsonArray a ? a.OfType<JsonValue>().Select(v => v.TryGetValue<string>(out var g) ? g : "").ToList() : null;

    /// <summary>{op: "open", ch, module, from}: a channel to one of the modules, if it's here and switched on.</summary>
    private void OpenChannel(JsonObject m, ServerUser user)
    {
        string id = Str(m, "module");
        if ((int?)m["ch"] is not int ch || channels.ContainsKey(ch)) { Error(m, "That channel number is in use."); return; }
        var module = server.Catalog.Find(id);
        if (module?.Instance is not { } instance)
        {
            Error(m, module == null ? $"{server.Settings.Name} doesn't have the {id} module." : $"The {module.Name} module is switched off on {server.Settings.Name}.");
            return;
        }
        if (user.Anonymous && !instance.Anonymous) { Error(m, $"Sign in to use {module.Name}."); return; }
        var c = new ModuleChannel(this, ch, module.Id, user, Client, Clean(Str(m, "from"), 40));
        channels[ch] = c;
        Reply(m, new JsonObject { ["op"] = "opened", ["ch"] = ch });
        server.RaiseChanged();
        _ = Task.Run(async () =>
        {
            try { await instance.ServeAsync(c, stop.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ModuleLog.Server.Error($"Module {module.Name}: {Describe()}'s channel failed", ex); }
            finally
            {
                // (ended by the module: the client is told)
                if (channels.TryRemove(ch, out _)) Post(new JsonObject { ["op"] = "close", ["ch"] = ch });
                c.End();
                server.RaiseChanged();
            }
        });
    }

    private sealed class ModuleChannel(Session session, int id, string module, ServerUser user, string client, string from) : IModuleChannel
    {
        private readonly Channel<JsonObject> inbox = Channel.CreateUnbounded<JsonObject>(new UnboundedChannelOptions { SingleReader = true });

        public string Module => module;
        public ServerUser User => user;
        public string Client => client;
        public string From => from;
        public string Address => session.Address;

        public void Post(JsonNode message) => session.Post(new JsonObject { ["ch"] = id, ["msg"] = message });

        public async Task<JsonObject?> ReadAsync(CancellationToken ct = default)
        {
            try
            {
                if (await inbox.Reader.WaitToReadAsync(ct) && inbox.Reader.TryRead(out var m)) return m;
            }
            catch (ChannelClosedException) { }
            return null;
        }

        public void Deliver(JsonObject m) => inbox.Writer.TryWrite(m);
        public void End() => inbox.Writer.TryComplete();

        public void Dispose()
        {
            if (session.channels.TryRemove(id, out _)) session.Post(new JsonObject { ["op"] = "close", ["ch"] = id });
            End();
        }
    }
}
