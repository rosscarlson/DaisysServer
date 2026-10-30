using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace DaisysServer.Protocol;

// This file is shared, word for word, by Daisy's Server (src/DaisysServer.Core/Protocol) and Daisy's App
// (src/DaisysApp.Core/Server): a change here goes there too. PROTOCOL.md describes it.

/// <summary>A problem worded for the user (the wrong password, the token not accepted, a module switched off…).</summary>
public sealed class ServerException(string message) : Exception(message);

/// <summary>
/// A client's connection to Daisy's Server: connected with a token, signed in as a user (the user's data key unwrapped
/// here, from the password), then any number of module channels on it at once, each a conversation with one of the
/// server's modules. Questions (<see cref="AskAsync"/>) carry an id ("rid") that the answer repeats.
/// </summary>
public sealed class ServerClient : IDisposable
{
    private readonly Wire wire;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<int, ServerChannel> channels = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> asks = new();
    private int nextChannel, nextAsk;
    private int closed;

    /// <summary>The server's name, its version, and the modules it has switched on.</summary>
    public string Server { get; private set; } = "";
    public string ServerVersion { get; private set; } = "";
    public IReadOnlyList<string> Modules { get; private set; } = Array.Empty<string>();
    /// <summary>Signed in as (the name as the server has it), and whether that's an admin.</summary>
    public string User { get; private set; } = "";
    public bool Admin { get; private set; }
    /// <summary>The user's data key: what modules seal their data with (<see cref="Vault.Seal"/>).</summary>
    public byte[] DataKey { get; private set; } = Array.Empty<byte>();

    /// <summary>The connection went (the reason, worded for the user); raised once, on a background thread.</summary>
    public event Action<string>? Closed;
    public bool IsClosed => closed == 1;

    private ServerClient(Wire wire) => this.wire = wire;

    /// <summary>Connects, proves the token, signs in (making the user's data key the first time) and starts listening.</summary>
    public static async Task<ServerClient> ConnectAsync(string host, int port, string token, string user, string password, string clientName, string app, CancellationToken ct)
    {
        Token.Parse(token);
        var tcp = new TcpClient { NoDelay = true };
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try { await tcp.ConnectAsync(host, port, timeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                tcp.Dispose();
                throw new ServerException($"No answer from {host}:{port}");
            }
            catch (SocketException ex)
            {
                tcp.Dispose();
                throw new ServerException($"Couldn't connect to {host}:{port}: {ex.Message.TrimEnd('.')}");
            }
        }
        Wire w;
        try { w = await Wire.ConnectAsync(tcp.GetStream(), token, ct); }
        catch (Exception ex) when (ex is IOException or FormatException or CryptographicException or InvalidOperationException or NullReferenceException or System.Text.Json.JsonException)
        {
            tcp.Dispose();
            throw new ServerException(ex is FormatException ? ex.Message : $"{host}:{port} isn't a Daisy's Server ({ex.Message.TrimEnd('.')})");
        }
        var c = new ServerClient(w);
        try
        {
            await w.SendAsync(new JsonObject { ["op"] = "hello", ["client"] = clientName, ["app"] = app, ["v"] = Wire.Version }, ct);
            var welcome = await c.ReplyAsync(ct, "The server didn't accept the token (or it's been revoked).");
            c.Server = Str(welcome, "server");
            c.ServerVersion = Str(welcome, "version");
            c.Modules = (welcome["modules"] as JsonArray)?.Select(m => (string?)m?["id"] ?? "").Where(m => m.Length > 0).ToList() ?? new List<string>();

            await w.SendAsync(new JsonObject { ["op"] = "prelogin", ["user"] = user }, ct);
            var pre = await c.ReplyAsync(ct);
            var master = Vault.Master(password, Convert.FromBase64String(Str(pre, "salt")), (int?)pre["iter"] ?? Vault.Iterations);
            await w.SendAsync(new JsonObject { ["op"] = "login", ["user"] = user, ["auth"] = Convert.ToBase64String(Vault.Auth(master)) }, ct);
            var login = await c.ReplyAsync(ct);
            c.User = Str(login, "user");
            c.Admin = (bool?)login["admin"] == true;
            var wrap = Vault.WrapKey(master);
            string vault = Str(login, "vault");
            if (vault.Length == 0)
            {
                // the first sign-in: this client makes the data key (the server keeps the first one it gets)
                await w.SendAsync(new JsonObject { ["op"] = "vault", ["vault"] = Vault.Seal(wrap, RandomNumberGenerator.GetBytes(32)) }, ct);
                vault = Str(await c.ReplyAsync(ct), "vault");
            }
            try { c.DataKey = Vault.Open(wrap, vault); }
            catch (Exception ex) when (ex is CryptographicException or FormatException) { throw new ServerException("Your data key couldn't be opened with this password."); }
        }
        catch
        {
            c.Dispose();
            throw;
        }
        _ = Task.Run(c.ReadLoopAsync);
        _ = Task.Run(c.PingLoopAsync);
        return c;
    }

    /// <summary>The next message during sign-in; an error from the server is thrown as it worded it.</summary>
    private async Task<JsonObject> ReplyAsync(CancellationToken ct, string? whenClosed = null)
    {
        JsonObject? m;
        try { m = await wire.ReadAsync(ct); }
        catch (Exception ex) when (ex is IOException or CryptographicException) { m = null; }
        if (m == null) throw new ServerException(whenClosed ?? "The server closed the connection.");
        if (Str(m, "op") == "error") throw new ServerException(Str(m, "message"));
        return m;
    }

    private async Task ReadLoopAsync()
    {
        string why = "The server closed the connection.";
        try
        {
            while (await wire.ReadAsync(stop.Token) is { } m)
            {
                if ((int?)m["ch"] is int ch && m["op"] == null)
                {
                    if (channels.TryGetValue(ch, out var c) && m["msg"] is JsonObject msg) c.Deliver(msg);
                    continue;
                }
                if ((int?)m["rid"] is int rid && asks.TryRemove(rid, out var ask)) { ask.TrySetResult(m); continue; }
                switch (Str(m, "op"))
                {
                    case "close" when (int?)m["ch"] is int gone:
                        if (channels.TryRemove(gone, out var ended)) ended.End(Str(m, "message"));
                        break;
                    case "bye":
                        why = Str(m, "message") is { Length: > 0 } s ? s : why;
                        return;
                }
            }
        }
        catch (OperationCanceledException) { why = "Disconnected."; }
        catch (Exception ex) { why = ex.Message; }
        finally { Close(why); }
    }

    private async Task PingLoopAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stop.Token);
                await wire.SendAsync(new JsonObject { ["op"] = "ping" }, stop.Token);
            }
        }
        catch { }
    }

    /// <summary>A question; its answer (an error comes back as ServerException).</summary>
    public async Task<JsonObject> AskAsync(JsonObject request, CancellationToken ct = default)
    {
        int rid = Interlocked.Increment(ref nextAsk);
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        asks[rid] = tcs;
        request["rid"] = rid;
        if (IsClosed) throw new ServerException("Not connected.");
        await wire.SendAsync(request, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using (timeout.Token.Register(() => tcs.TrySetCanceled()))
        {
            JsonObject r;
            try { r = await tcs.Task; }
            catch (TaskCanceledException) { asks.TryRemove(rid, out _); throw new ServerException(IsClosed ? "Not connected." : "The server didn't answer."); }
            if (Str(r, "op") == "error") throw new ServerException(Str(r, "message"));
            return r;
        }
    }

    /// <summary>Opens a channel to one of the server's modules: <paramref name="module"/> on the server, from this client's
    /// <paramref name="from"/> (its module's id). A module that's off or missing there is a ServerException.</summary>
    public async Task<ServerChannel> OpenAsync(string module, string from, CancellationToken ct = default)
    {
        int ch = Interlocked.Increment(ref nextChannel);
        var c = new ServerChannel(this, ch, module);
        channels[ch] = c;
        try { await AskAsync(new JsonObject { ["op"] = "open", ["ch"] = ch, ["module"] = module, ["from"] = from }, ct); }
        catch
        {
            channels.TryRemove(ch, out _);
            throw;
        }
        return c;
    }

    internal void Send(JsonNode m)
    {
        if (IsClosed) return;
        _ = SendQuietAsync(m);
    }

    private readonly SemaphoreSlim order = new(1, 1);

    private async Task SendQuietAsync(JsonNode m)
    {
        // (in the order they were posted)
        await order.WaitAsync();
        try { await wire.SendAsync(m, stop.Token); }
        catch { }
        finally { order.Release(); }
    }

    internal void Closing(ServerChannel c)
    {
        if (channels.TryRemove(c.Id, out _)) Send(new JsonObject { ["op"] = "close", ["ch"] = c.Id });
    }

    /// <summary>Changes the password: the data key wrapped again with the new one (nothing else changes).</summary>
    public async Task ChangePasswordAsync(string oldPassword, string newPassword, CancellationToken ct = default)
    {
        var pre = await AskAsync(new JsonObject { ["op"] = "prelogin", ["user"] = User }, ct);
        var oldMaster = Vault.Master(oldPassword, Convert.FromBase64String(Str(pre, "salt")), (int?)pre["iter"] ?? Vault.Iterations);
        var salt = RandomNumberGenerator.GetBytes(16);
        var master = Vault.Master(newPassword, salt, Vault.Iterations);
        await AskAsync(new JsonObject
        {
            ["op"] = "password", ["auth"] = Convert.ToBase64String(Vault.Auth(oldMaster)),
            ["salt"] = Convert.ToBase64String(salt), ["iter"] = Vault.Iterations,
            ["newAuth"] = Convert.ToBase64String(Vault.Auth(master)), ["vault"] = Vault.Seal(Vault.WrapKey(master), DataKey),
        }, ct);
    }

    private void Close(string why)
    {
        if (Interlocked.Exchange(ref closed, 1) == 1) return;
        stop.Cancel();
        wire.Dispose();
        foreach (var c in channels.Values) c.End(why);
        channels.Clear();
        foreach (var a in asks.Values) a.TrySetCanceled();
        Closed?.Invoke(why);
    }

    public void Dispose() => Close("Disconnected.");

    public static string Str(JsonObject m, string name) => m[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
}

/// <summary>One conversation with a server module, on a <see cref="ServerClient"/>'s connection.</summary>
public sealed class ServerChannel : IDisposable
{
    private readonly ServerClient client;
    private readonly Channel<JsonObject> inbox = Channel.CreateUnbounded<JsonObject>(new UnboundedChannelOptions { SingleReader = true });

    public int Id { get; }
    public string Module { get; }
    /// <summary>Why it ended (the server's message, or the connection's), once it has.</summary>
    public string Ended { get; private set; } = "";

    internal ServerChannel(ServerClient client, int id, string module)
    {
        this.client = client;
        Id = id;
        Module = module;
    }

    public string Server => client.Server;
    public byte[] DataKey => client.DataKey;

    /// <summary>Sends a message to the module (queued: they go in order).</summary>
    public void Post(JsonNode message) => client.Send(new JsonObject { ["ch"] = Id, ["msg"] = message });

    /// <summary>The module's next message; null once the channel or the connection has closed.</summary>
    public async Task<JsonObject?> ReadAsync(CancellationToken ct = default)
    {
        try
        {
            if (await inbox.Reader.WaitToReadAsync(ct) && inbox.Reader.TryRead(out var m)) return m;
        }
        catch (ChannelClosedException) { }
        return null;
    }

    internal void Deliver(JsonObject m) => inbox.Writer.TryWrite(m);

    internal void End(string why)
    {
        if (Ended.Length == 0) Ended = why.Length > 0 ? why : "closed";
        inbox.Writer.TryComplete();
    }

    public void Dispose()
    {
        client.Closing(this);
        End("closed");
    }
}
