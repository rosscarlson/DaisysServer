using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DaisysServer.Modules;
using DaisysServer.Protocol;

namespace DaisysServer.Modules.Sessions;

/// <summary>
/// Every session shared: the index (each one's meta, kept as the JSON object it came as, so newer clients' fields
/// survive, plus who shared it: user id, address, token), one file a session (the client's gzip, kept as it came), the
/// uploads under way, the bans, and the open channels the news goes to. One for the whole server, not sealed:
/// data\sessions\sessions.json, bans.json, &lt;id&gt;.gz, and uploads\&lt;id&gt;.part. Callable from any thread; pushes are
/// posted under the lock, so every channel gets them in order.
/// </summary>
internal sealed class SessionStore
{
    public const long MaxSize = 200L << 20;
    public const int MaxPiece = 1 << 20;
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromMinutes(10);
    /// <summary>What the server sets: never taken from a client's meta.</summary>
    private static readonly string[] ServerFields = ["id", "user", "client", "shared", "size", "mine", "userId", "address", "tokenId", "token"];

    /// <summary>A client's sessions channel.</summary>
    public sealed class Peer(IModuleChannel channel)
    {
        public IModuleChannel Channel { get; } = channel;
        public readonly List<Upload> Uploads = new();
    }

    /// <summary>A share under way: the meta it'll have, and its bytes so far in uploads\&lt;id&gt;.part.</summary>
    public sealed class Upload
    {
        public required string Id;
        public required JsonObject Meta;
        public required long Size;
        public required string Sha256;
        public required string Part;
        public required Peer Peer;
        public long Received;
        public DateTime Began = DateTime.UtcNow;
        public IncrementalHash Hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    }

    public enum BanKind { User, PC, Address, Token }

    /// <summary>One ban: what (a user's id, a PC's name, an IP address or range like 203.0.113.0/24, a token's id),
    /// shown as <see cref="Name"/>.</summary>
    public sealed class Ban
    {
        public BanKind Kind { get; set; }
        public string Value { get; set; } = "";
        public string Name { get; set; } = "";
        public string Note { get; set; } = "";
        public DateTime At { get; set; }
    }

    private sealed class Index { public List<JsonObject> Sessions { get; set; } = new(); }
    private sealed class Bans { public List<Ban> List { get; set; } = new(); }

    /// <summary>A session as the page shows it.</summary>
    public sealed record Row(string Id, DateTime Shared, string Title, string Description, string Driver, string Game, string Track, string Car,
        string Class, int Laps, double Best, string User, string UserId, string Client, string Address, string TokenId, string Token, long Size, string Video);

    private readonly object gate = new();
    private readonly string folder, indexFile, bansFile, uploads;
    private readonly ModuleLog log;
    private readonly Dictionary<string, JsonObject> sessions = new();
    private readonly List<Ban> bans;
    private readonly List<Peer> peers = new();
    private readonly Timer saver, sweeper;
    private bool dirty;

    /// <summary>A session or a ban came, changed or went (any thread).</summary>
    public event Action? Changed;

    public SessionStore(ModuleLog log)
    {
        this.log = log;
        folder = Path.Combine(JsonFile.DataFolder, "sessions");
        indexFile = Path.Combine(folder, "sessions.json");
        bansFile = Path.Combine(folder, "bans.json");
        uploads = Path.Combine(folder, "uploads");
        foreach (var s in JsonFile.Load<Index>(indexFile).Sessions)
            if (Str(s, "id") is { Length: > 0 } id && File.Exists(FileOf(id))) sessions[id] = s;
        bans = JsonFile.Load<Bans>(bansFile).List;
        // (uploads left by a stop are never finished)
        try { if (Directory.Exists(uploads)) Directory.Delete(uploads, true); } catch { }
        saver = new Timer(_ => Flush());
        sweeper = new Timer(_ => Sweep(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public string Folder => folder;
    private string FileOf(string id) => Path.Combine(folder, id + ".gz");

    private static string Str(JsonObject o, string name) => ServerClient.Str(o, name);
    private static long Long(JsonObject o, string name) => o[name] is JsonValue v ? v.TryGetValue<long>(out var n) ? n : v.TryGetValue<double>(out var d) ? (long)d : 0 : 0;
    private static double Double(JsonObject o, string name) => o[name] is JsonValue v ? v.TryGetValue<double>(out var d) ? d : v.TryGetValue<long>(out var n) ? n : 0 : 0;
    private static long Ms(DateTime utc) => (long)(utc - DateTime.UnixEpoch).TotalMilliseconds;

    private void Save()
    {
        dirty = true;
        saver.Change(1000, Timeout.Infinite);
        Changed?.Invoke();
    }

    public void Flush()
    {
        Index index;
        lock (gate)
        {
            if (!dirty) return;
            dirty = false;
            index = new Index { Sessions = sessions.Values.Select(s => (JsonObject)s.DeepClone()).ToList() };
        }
        JsonFile.Save(indexFile, index);
    }

    private void SaveBans()
    {
        List<Ban> copy;
        lock (gate) copy = bans.ToList();
        JsonFile.Save(bansFile, new Bans { List = copy });
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- the channels

    public void Join(Peer p) { lock (gate) peers.Add(p); }

    public void Leave(Peer p)
    {
        lock (gate)
        {
            peers.Remove(p);
            foreach (var u in p.Uploads.ToList()) Drop(u);
        }
    }

    /// <summary>The meta a channel's user is sent: the client's fields, the server's, and mine (theirs or not); not
    /// where it came from.</summary>
    private static JsonObject MetaFor(JsonObject s, IModuleChannel to)
    {
        var m = new JsonObject();
        foreach (var (k, v) in s)
            if (k is not ("userId" or "address" or "tokenId" or "token")) m[k] = v?.DeepClone();
        m["mine"] = Str(s, "userId") == to.User.Id;
        return m;
    }

    private void Push(string op, JsonObject? session, string? id = null)
    {
        foreach (var p in peers)
            p.Channel.Post(session != null ? new JsonObject { ["op"] = op, ["session"] = MetaFor(session, p.Channel) } : new JsonObject { ["op"] = op, ["id"] = id });
    }

    // ---------------------------------------------------------------- what clients ask

    public JsonArray List(IModuleChannel to)
    {
        lock (gate) return new JsonArray(sessions.Values.OrderByDescending(s => Long(s, "shared")).Select(s => (JsonNode)MetaFor(s, to)).ToArray());
    }

    /// <summary>A share begins: its id, or the problem.</summary>
    public (string? Id, string? Problem) Begin(Peer p, JsonObject? meta, long size, string sha256)
    {
        var c = p.Channel;
        if (BanOf(c) is { } ban) return (null, Refused(ban));
        if (meta == null) return (null, "That session came without its details.");
        if (size <= 0) return (null, "That session is empty.");
        if (size > MaxSize) return (null, "That session is too big to share (over 200 MB).");
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) return (null, "That session came without its checksum.");
        if (meta.ToJsonString().Length > 64 << 10) return (null, "That session's details are too long.");
        var m = new JsonObject();
        foreach (var (k, v) in meta)
            if (!ServerFields.Contains(k)) m[k] = v?.DeepClone();
        Tidy(m);
        string id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        Directory.CreateDirectory(uploads);
        var u = new Upload { Id = id, Meta = m, Size = size, Sha256 = sha256.ToLowerInvariant(), Part = Path.Combine(uploads, id + ".part"), Peer = p };
        File.WriteAllBytes(u.Part, []);
        lock (gate)
        {
            if (p.Uploads.Count >= 4) Drop(p.Uploads[0]);
            p.Uploads.Add(u);
        }
        return (id, null);
    }

    /// <summary>One piece of a share: the bytes received, or the problem (the upload's then dropped).</summary>
    public (long Received, string? Problem) Put(Peer p, string id, long offset, string data, bool eof)
    {
        Upload? u;
        lock (gate) u = p.Uploads.FirstOrDefault(x => x.Id == id);
        if (u == null) return (0, "That share isn't under way (it may have taken too long): share it again.");
        string? Fail(string why)
        {
            lock (gate) Drop(u);
            return why;
        }
        if (offset != u.Received) return (0, Fail("A piece of that session went missing: share it again."));
        byte[] bytes;
        try { bytes = Convert.FromBase64String(data); }
        catch (FormatException) { return (0, Fail("A piece of that session was damaged: share it again.")); }
        if (u.Received + bytes.Length > u.Size) return (0, Fail("That session was bigger than it said: share it again."));
        try
        {
            using (var f = new FileStream(u.Part, FileMode.Append, FileAccess.Write)) f.Write(bytes);
        }
        catch (IOException ex) { return (0, Fail("The server couldn't keep it: " + ex.Message)); }
        u.Hash.AppendData(bytes);
        u.Received += bytes.Length;
        if (!eof) return (u.Received, null);
        if (u.Received != u.Size) return (0, Fail("That session arrived short: share it again."));
        if (Convert.ToHexString(u.Hash.GetHashAndReset()).ToLowerInvariant() != u.Sha256) return (0, Fail("That session arrived damaged: share it again."));
        var c = p.Channel;
        var s = u.Meta;
        s["id"] = u.Id;
        s["user"] = c.User.Name;
        s["client"] = c.Client;
        s["shared"] = Ms(DateTime.UtcNow);
        s["size"] = u.Size;
        s["userId"] = c.User.Id;
        s["address"] = c.Address;
        s["tokenId"] = c.TokenId;
        s["token"] = c.TokenName;
        lock (gate)
        {
            p.Uploads.Remove(u);
            try { File.Move(u.Part, FileOf(u.Id), overwrite: true); }
            catch (IOException ex) { File.Delete(u.Part); return (0, "The server couldn't keep it: " + ex.Message); }
            sessions[u.Id] = s;
            Push("added", s);
            Save();
        }
        log.Info($"{c.User.Name} on {c.Client} ({c.Address}) shared {u.Id}, {Ui.Size(u.Size)}: {Str(s, "track")}, \"{Str(s, "title")}\"");
        return (u.Received, null);
    }

    /// <summary>A piece of a session's file: (its size, the piece), or the problem.</summary>
    public (long Size, byte[] Data, string? Problem) Get(IModuleChannel c, string id, long offset, int length)
    {
        lock (gate)
            if (!sessions.ContainsKey(id)) return (0, [], "That session isn't on the server any more.");
        try
        {
            using var f = new FileStream(FileOf(id), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (offset < 0 || offset > f.Length) return (0, [], "That's past the end of the session.");
            if (offset == 0) log.Info($"{c.User.Name} on {c.Client} ({c.Address}) downloads {id}, {Ui.Size(f.Length)}");
            f.Position = offset;
            var buffer = new byte[(int)Math.Min(Math.Clamp(length, 0, MaxPiece), f.Length - offset)];
            f.ReadExactly(buffer);
            return (f.Length, buffer, null);
        }
        catch (FileNotFoundException) { return (0, [], "That session isn't on the server any more."); }
    }

    /// <summary>The sharer (or an admin) changes the title, description and video: the meta now, or the problem.</summary>
    public (JsonObject? Meta, string? Problem) Edit(IModuleChannel c, JsonObject m)
    {
        string id = Str(m, "id");
        JsonObject s;
        lock (gate)
        {
            if (!sessions.TryGetValue(id, out s!)) return (null, "That session isn't on the server any more.");
            if (Str(s, "userId") != c.User.Id && !c.User.Admin) return (null, "Only the person who shared it can change it.");
            if (!c.User.Admin && BanOf(c) is { } ban) return (null, Refused(ban));
            foreach (var k in new[] { "title", "description", "video", "videoAt" })
                if (m.ContainsKey(k)) s[k] = m[k]?.DeepClone();
            Tidy(s);
            Push("changed", s);
            Save();
        }
        log.Info($"{c.User.Name} on {c.Client} ({c.Address}) changed {id}: \"{Str(s, "title")}\"");
        return (MetaFor(s, c), null);
    }

    /// <summary>The sharer (or an admin) takes one off: null, or the problem.</summary>
    public string? Delete(IModuleChannel c, string id)
    {
        lock (gate)
        {
            if (!sessions.TryGetValue(id, out var s)) return null;
            if (Str(s, "userId") != c.User.Id && !c.User.Admin) return "Only the person who shared it can change it.";
        }
        Remove([id], $"{c.User.Name} on {c.Client} ({c.Address})");
        return null;
    }

    /// <summary>Text clipped to sensible lengths, and a video only when it's a web link.</summary>
    private static void Tidy(JsonObject m)
    {
        foreach (var (k, max) in new[] { ("title", 200), ("description", 4000), ("driver", 80), ("game", 40), ("track", 120), ("car", 120), ("class", 40) })
            if (m[k] is JsonValue v && v.TryGetValue<string>(out var t) && t.Length > max) m[k] = t[..max];
        if (m["video"] is JsonNode video && !(video is JsonValue vv && vv.TryGetValue<string>(out var url) && url.Length <= 500
            && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
            m["video"] = null;
    }

    private void Drop(Upload u)
    {
        u.Peer.Uploads.Remove(u);
        u.Hash.Dispose();
        try { File.Delete(u.Part); } catch { }
    }

    /// <summary>Uploads that never finished, after 10 minutes.</summary>
    private void Sweep()
    {
        lock (gate)
            foreach (var u in peers.SelectMany(p => p.Uploads).Where(u => DateTime.UtcNow - u.Began > UploadTimeout).ToList())
            {
                log.Info($"{u.Peer.Channel.User.Name} on {u.Peer.Channel.Client}: a share that didn't finish in 10 minutes, dropped");
                Drop(u);
            }
    }

    // ---------------------------------------------------------------- the page: finding, removing, banning

    public int Count { get { lock (gate) return sessions.Count; } }
    public long TotalSize { get { lock (gate) return sessions.Values.Sum(s => Long(s, "size")); } }

    public List<Row> Rows()
    {
        lock (gate)
            return sessions.Values.Select(s => new Row(Str(s, "id"), DateTime.UnixEpoch.AddMilliseconds(Long(s, "shared")).ToLocalTime(),
                Str(s, "title"), Str(s, "description"), Str(s, "driver"), Str(s, "game"), Str(s, "track"), Str(s, "car"), Str(s, "class"),
                (int)Long(s, "laps"), Double(s, "best"), Str(s, "user"), Str(s, "userId"),
                Str(s, "client"), Str(s, "address"), Str(s, "tokenId"), Str(s, "token"), Long(s, "size"), Str(s, "video")))
                .OrderByDescending(r => r.Shared).ToList();
    }

    /// <summary>Takes sessions off the server (their files too), telling every client.</summary>
    public int Remove(IEnumerable<string> ids, string by)
    {
        int n = 0;
        lock (gate)
            foreach (var id in ids.ToList())
            {
                if (!sessions.Remove(id, out var s)) continue;
                try { File.Delete(FileOf(id)); } catch (IOException ex) { log.Warn($"{id}.gz couldn't be deleted", ex); }
                Push("removed", null, id);
                log.Info($"{by} deleted {id} ({Str(s, "user")}'s, {Str(s, "track")}, \"{Str(s, "title")}\")");
                n++;
            }
        if (n > 0) Save();
        return n;
    }

    public List<Ban> BanList() { lock (gate) return bans.ToList(); }

    /// <summary>Bans someone from sharing, and takes off every session that matches the ban; how many went.</summary>
    public int AddBan(BanKind kind, string value, string name, string note)
    {
        value = value.Trim();
        if (value.Length == 0) return 0;
        var ban = new Ban { Kind = kind, Value = value, Name = name.Length > 0 ? name : value, Note = note, At = DateTime.UtcNow };
        lock (gate)
        {
            bans.RemoveAll(b => b.Kind == kind && string.Equals(b.Value, value, StringComparison.OrdinalIgnoreCase));
            bans.Add(ban);
        }
        SaveBans();
        log.Info($"Banned from sharing: {kind} {ban.Name}{(note.Length > 0 ? $" ({note})" : "")}");
        List<string> theirs;
        lock (gate) theirs = sessions.Values.Where(s => Matches(ban, Str(s, "userId"), Str(s, "client"), Str(s, "address"), Str(s, "tokenId"))).Select(s => Str(s, "id")).ToList();
        return Remove(theirs, "The ban on " + ban.Name);
    }

    public void Unban(Ban ban)
    {
        lock (gate) bans.Remove(ban);
        SaveBans();
        log.Info($"Unbanned: {ban.Kind} {ban.Name}");
    }

    private Ban? BanOf(IModuleChannel c)
    {
        lock (gate) return bans.FirstOrDefault(b => Matches(b, c.User.Id, c.Client, c.Address, c.TokenId));
    }

    private static string Refused(Ban ban) => "You can't share sessions on this server.";

    public static bool Matches(Ban b, string userId, string client, string address, string tokenId) => b.Kind switch
    {
        BanKind.User => b.Value == userId,
        BanKind.PC => string.Equals(b.Value, client, StringComparison.OrdinalIgnoreCase),
        BanKind.Token => b.Value == tokenId,
        _ => InRange(b.Value, address),
    };

    /// <summary>An address, or a range written as a.b.c.d/n (IPv6 too).</summary>
    public static bool InRange(string range, string address)
    {
        if (!IPAddress.TryParse(address, out var ip)) return false;
        if (range.Contains('/')) return IPNetwork.TryParse(range, out var net) && net.Contains(ip);
        return IPAddress.TryParse(range, out var one) && one.Equals(ip);
    }

    public static bool ValidAddress(string range) => range.Contains('/') ? IPNetwork.TryParse(range, out _) : IPAddress.TryParse(range, out _);

    public void Dispose()
    {
        sweeper.Dispose();
        saver.Dispose();
        Flush();
    }
}
