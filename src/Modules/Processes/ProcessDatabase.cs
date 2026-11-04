using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using DaisysServer.Modules;
using DaisysServer.Protocol;

namespace DaisysServer.Modules.Processes;

/// <summary>
/// The master database: every program known (Daisy's App's <c>KnownProcess</c>, kept as the JSON object it came as, so
/// newer clients' fields survive), the version that counts its changes (each changed entry's <c>rev</c>), and the PCs
/// that sync with it (their version, and their usage figures, averaged into entries' <c>fleet*</c>). One for the whole
/// server, not per user, and not sealed: data\processes\processes.json and clients.json, written a second after a
/// change. Callable from any thread; what goes to the clients is sent under the lock, so each gets every change in order.
/// </summary>
internal sealed class ProcessDatabase
{
    public const string Format = "DaisysApp.Processes";
    public const int MaxEntryChars = 32 << 10, MaxPerMessage = 1000;
    private static readonly string[] Fleet = ["fleetCpu", "fleetMemoryMB", "fleetPcs"];
    /// <summary>Usage older than this doesn't count towards the fleet's figures.</summary>
    private static readonly TimeSpan UsageKept = TimeSpan.FromDays(30);

    /// <summary>A client's processes channel.</summary>
    public sealed class Peer(IModuleChannel channel)
    {
        public IModuleChannel Channel { get; } = channel;
        public bool Joined;
        public void Send(JsonObject m) => Channel.Post(m);
    }

    /// <summary>One program's figures from one PC (its latest).</summary>
    public sealed class Usage
    {
        public double Cpu { get; set; }
        public double PeakCpu { get; set; }
        public double MemoryMB { get; set; }
        public double PeakMemoryMB { get; set; }
        public long Samples { get; set; }
        public DateTime At { get; set; }
    }

    /// <summary>A PC that syncs: its user and app when last joined, the version it has, and its usage figures.</summary>
    public sealed class Client
    {
        public string Name { get; set; } = "";
        public string User { get; set; } = "";
        public string App { get; set; } = "";
        public long Version { get; set; }
        public DateTime LastSeen { get; set; }
        public Dictionary<string, Usage> Usage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class Store
    {
        public long Version { get; set; }
        public List<JsonObject> Entries { get; set; } = new();
    }

    public sealed class ClientStore
    {
        public List<Client> Clients { get; set; } = new();
        /// <summary>When the fleet's figures last went out with a new version (once a day at most).</summary>
        public DateTime FleetSent { get; set; }
    }

    /// <summary>An entry as the page shows it.</summary>
    public sealed record Row(string Name, string Title, string Threat, string Needed, string By, DateTime Updated, long Rev, bool Reviewed, string Publisher);

    /// <summary>A PC as the page shows it.</summary>
    public sealed record Pc(string Name, string User, string App, long Version, DateTime LastSeen, bool Connected);

    private readonly object gate = new();
    private readonly string folder, file, clientsFile;
    private readonly ModuleLog log;
    private readonly Dictionary<string, JsonObject> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Client> clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> fleetChanged = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Peer> peers = new();
    private readonly Timer saver;
    private DateTime fleetSent;
    private bool dirty, clientsDirty;

    public long Version { get; private set; }

    /// <summary>Something changed: an entry, or a PC (any thread).</summary>
    public event Action? Changed;

    public ProcessDatabase(ModuleLog log)
    {
        this.log = log;
        folder = Path.Combine(JsonFile.DataFolder, "processes");
        file = Path.Combine(folder, "processes.json");
        clientsFile = Path.Combine(folder, "clients.json");
        var store = JsonFile.Load<Store>(file);
        Version = store.Version;
        foreach (var e in store.Entries)
            if (Key(Str(e, "name")) is { Length: > 0 } k) { e["name"] = k; entries[k] = e; }
        var cs = JsonFile.Load<ClientStore>(clientsFile);
        foreach (var c in cs.Clients.Where(c => c.Name.Length > 0)) clients[c.Name] = c;
        fleetSent = cs.FleetSent;
        saver = new Timer(_ => Flush());
    }

    public string Folder => folder;

    // ---------------------------------------------------------------- the entries

    private static string Str(JsonObject o, string name) => ServerClient.Str(o, name);

    /// <summary>The key: lower case, no ".exe" (as the client's KnownProcess.Key).</summary>
    public static string Key(string name)
    {
        name = name.Trim().ToLowerInvariant();
        return name.EndsWith(".exe") ? name[..^4] : name;
    }

    private static bool Reviewed(JsonObject e) => e["reviewed"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static DateTime Updated(JsonObject e) =>
        DateTime.TryParse(Str(e, "updated"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : DateTime.MinValue;

    private static long Long(JsonObject e, string name) => e[name] is JsonValue v && v.TryGetValue<long>(out var n) ? n : 0;

    /// <summary>Whether <paramref name="incoming"/> replaces <paramref name="stored"/>: a reviewed one beats an automatic
    /// one, then the newer <c>updated</c> (a tie keeps the stored one).</summary>
    private static bool Wins(JsonObject incoming, JsonObject? stored)
    {
        if (stored == null) return true;
        if (Reviewed(incoming) != Reviewed(stored)) return Reviewed(incoming);
        return Updated(incoming) > Updated(stored);
    }

    /// <summary>Merges one entry (a client's or an import's); its key when it changed what's kept. The fleet figures are
    /// the server's own, so they stay; the caller numbers it.</summary>
    private string? Merge(JsonNode? node)
    {
        if (node is not JsonObject o || Key(Str(o, "name")) is not { Length: > 0 and <= 260 } key) return null;
        if (o.ToJsonString().Length > MaxEntryChars) return null;
        var incoming = (JsonObject)o.DeepClone();
        entries.TryGetValue(key, out var stored);
        if (!Wins(incoming, stored)) return null;
        incoming["name"] = key;
        foreach (var f in Fleet) incoming.Remove(f);
        if (stored == null) ApplyFleet(key, incoming);
        else foreach (var f in Fleet) if (stored[f] is { } v) incoming[f] = v.DeepClone();
        entries[key] = incoming;
        fleetChanged.Remove(key);
        return key;
    }

    /// <summary>Gives the changed entries a new version, keeps them, and sends them to every client.</summary>
    private void Publish(ICollection<string> changed, Peer? sender, IEnumerable<string>? alsoToSender = null)
    {
        Version++;
        foreach (var k in changed) entries[k]["rev"] = Version;
        var list = Entries(changed);
        foreach (var p in peers.Where(p => p.Joined))
        {
            var m = Changes(false, p == sender && alsoToSender != null ? Entries(changed.Concat(alsoToSender)) : (JsonArray)list.DeepClone());
            p.Send(m);
        }
        Save(entries: true);
    }

    private JsonArray Entries(IEnumerable<string> keys)
    {
        var a = new JsonArray();
        foreach (var k in keys.Distinct(StringComparer.OrdinalIgnoreCase))
            if (entries.TryGetValue(k, out var e)) a.Add(e.DeepClone());
        return a;
    }

    private JsonObject Changes(bool full, JsonArray list) => new() { ["op"] = "changes", ["version"] = Version, ["full"] = full, ["entries"] = list };

    // ---------------------------------------------------------------- the channel

    public void Handle(Peer from, JsonObject m)
    {
        lock (gate)
        {
            string op = Str(m, "op");
            if (op == "join") { Join(from, m); return; }
            if (!from.Joined) return;
            switch (op)
            {
                case "seen": Seen(from, Long(m, "version")); break;
                case "report": Report(from, m["entries"] as JsonArray); break;
                case "usage": UsageFrom(from, m["items"] as JsonArray); break;
                default: from.Send(new JsonObject { ["op"] = "error", ["message"] = $"This server can't do \"{op}\" yet: update it." }); break;
            }
        }
        Changed?.Invoke();
    }

    private Client ClientOf(Peer p)
    {
        string name = p.Channel.Client.Length > 0 ? p.Channel.Client : "?";
        if (!clients.TryGetValue(name, out var c)) clients[name] = c = new Client { Name = name };
        c.User = p.Channel.User.Name;
        c.LastSeen = DateTime.UtcNow;
        clientsDirty = true;
        return c;
    }

    /// <summary>{op: "join", version, app}: every change since that version, or everything (full) for a new client or
    /// one that synced with another server (a version past this one's).</summary>
    private void Join(Peer p, JsonObject m)
    {
        if (p.Joined) return;
        long since = Long(m, "version");
        var c = ClientOf(p);
        if (Str(m, "app") is { Length: > 0 } app) c.App = app.Length > 40 ? app[..40] : app;
        bool full = since <= 0 || since > Version;
        var list = new JsonArray();
        foreach (var e in entries.Values)
            if (full || Long(e, "rev") > since) list.Add(e.DeepClone());
        p.Joined = true;
        peers.Add(p);
        p.Send(Changes(full, list));
        var pcs = new JsonArray();
        foreach (var k in clients.Values.OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase))
            pcs.Add(new JsonObject { ["name"] = k.Name, ["version"] = k.Version, ["lastSeen"] = k.LastSeen });
        p.Send(new JsonObject { ["op"] = "clients", ["clients"] = pcs });
        log.Info($"{p.Channel.User.Name} on {p.Channel.Client} joined at version {since}: sent {list.Count}{(full ? " (all)" : "")}");
        Save(entries: false);
    }

    public void Leave(Peer p)
    {
        lock (gate)
        {
            if (!peers.Remove(p)) return;
            if (clients.TryGetValue(p.Channel.Client, out var c)) { c.LastSeen = DateTime.UtcNow; Save(entries: false); }
        }
        Changed?.Invoke();
    }

    private void Seen(Peer p, long version)
    {
        var c = ClientOf(p);
        c.Version = Math.Min(Math.Max(version, 0), Version);
        Save(entries: false);
    }

    /// <summary>{op: "report", entries}: each merged; the changed ones numbered and sent to everyone, the sender too
    /// (its acknowledgement, with this server's copy of those that didn't change).</summary>
    private void Report(Peer from, JsonArray? list)
    {
        if (list == null) return;
        var sent = new List<string>();
        var changed = new List<string>();
        foreach (var node in list.Take(MaxPerMessage))
        {
            if (node is JsonObject o && Key(Str(o, "name")) is { Length: > 0 } k) sent.Add(k);
            if (Merge(node) is { } key) changed.Add(key);
        }
        var same = sent.Except(changed, StringComparer.OrdinalIgnoreCase).ToList();
        if (changed.Count > 0)
        {
            Publish(changed, from, same);
            log.Info($"{from.Channel.User.Name} on {from.Channel.Client}: {changed.Count} changed, version {Version}");
        }
        else if (same.Count > 0) from.Send(Changes(false, Entries(same)));
    }

    // ---------------------------------------------------------------- usage: the fleet's figures

    private static double Num(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? Math.Max(d, 0) : 0;

    /// <summary>{op: "usage", items}: this PC's latest figures; the fleet's averages are worked out again, and go out with
    /// a new version once a day (until then they ride along with any change to the entry).</summary>
    private void UsageFrom(Peer from, JsonArray? items)
    {
        if (items == null) return;
        var c = ClientOf(from);
        var now = DateTime.UtcNow;
        var names = new List<string>();
        foreach (var node in items.Take(5000))
        {
            if (node is not JsonObject o || Key(Str(o, "name")) is not { Length: > 0 and <= 260 } k) continue;
            c.Usage[k] = new Usage
            {
                Cpu = Num(o, "cpu"), PeakCpu = Num(o, "peakCpu"), MemoryMB = Num(o, "memoryMB"), PeakMemoryMB = Num(o, "peakMemoryMB"),
                Samples = (long)Num(o, "samples"), At = now,
            };
            names.Add(k);
        }
        foreach (var k in names)
            if (entries.TryGetValue(k, out var e) && ApplyFleet(k, e)) fleetChanged.Add(k);
        if (fleetChanged.Count > 0 && now - fleetSent > TimeSpan.FromDays(1))
        {
            fleetSent = now;
            var keys = fleetChanged.ToList();
            fleetChanged.Clear();
            Publish(keys, null);
            log.Info($"The fleet's figures for {keys.Count} programs went out, version {Version}");
        }
        Save(entries: true);
    }

    /// <summary>Puts the fleet's averages for <paramref name="key"/> on the entry: whether they changed.</summary>
    private bool ApplyFleet(string key, JsonObject e)
    {
        var since = DateTime.UtcNow - UsageKept;
        var all = clients.Values.Select(c => c.Usage.GetValueOrDefault(key)).Where(u => u != null && u.At > since).ToList();
        double? cpu = all.Count > 0 ? Math.Round(all.Average(u => u!.Cpu), 2) : null;
        double? mem = all.Count > 0 ? Math.Round(all.Average(u => u!.MemoryMB), 1) : null;
        int? pcs = all.Count > 0 ? all.Count : null;
        double? oldCpu = e["fleetCpu"] is JsonValue a && a.TryGetValue<double>(out var x) ? x : null;
        double? oldMem = e["fleetMemoryMB"] is JsonValue b && b.TryGetValue<double>(out var y) ? y : null;
        int? oldPcs = e["fleetPcs"] is JsonValue d && d.TryGetValue<int>(out var z) ? z : null;
        if (cpu == oldCpu && mem == oldMem && pcs == oldPcs) return false;
        if (pcs == null) foreach (var f in Fleet) e.Remove(f);
        else { e["fleetCpu"] = cpu; e["fleetMemoryMB"] = mem; e["fleetPcs"] = pcs; }
        return true;
    }

    // ---------------------------------------------------------------- the page: export, import, what's shown

    public List<Row> Rows()
    {
        lock (gate)
            return entries.Values.Select(e => new Row(Str(e, "name"), Str(e, "title"), Str(e, "threat"), Str(e, "necessity"), Str(e, "by"),
                Updated(e), Long(e, "rev"), Reviewed(e), Str(e, "publisher"))).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public int Count { get { lock (gate) return entries.Count; } }

    public List<Pc> Pcs()
    {
        lock (gate)
        {
            var on = peers.Where(p => p.Joined).Select(p => p.Channel.Client).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return clients.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new Pc(c.Name, c.User, c.App, c.Version, c.LastSeen, on.Contains(c.Name))).ToList();
        }
    }

    /// <summary>The database as Daisy's App's export file (the same format as its Process Analyzer's Export…).</summary>
    public string Export()
    {
        lock (gate)
        {
            var list = new JsonArray();
            foreach (var e in entries.Values.OrderBy(e => Str(e, "name"), StringComparer.Ordinal)) list.Add(e.DeepClone());
            return new JsonObject
            {
                ["format"] = Format, ["formatVersion"] = 1, ["exported"] = DateTime.UtcNow, ["client"] = Environment.MachineName,
                ["serverVersion"] = Version, ["entries"] = list,
            }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>Merges an export (or a bare list of entries) by the usual rules: (read, changed); the changed ones go to
    /// every client with a new version.</summary>
    public (int Read, int Changed) Import(string json)
    {
        var node = JsonNode.Parse(json);
        var list = node as JsonArray ?? node?["entries"] as JsonArray ?? throw new FormatException("That isn't a process database export.");
        int changed;
        lock (gate)
        {
            var keys = new List<string>();
            foreach (var e in list) if (Merge(e) is { } k) keys.Add(k);
            changed = keys.Count;
            if (changed > 0) Publish(keys, null);
        }
        log.Info($"Imported {list.Count} entries: {changed} changed, version {Version}");
        Changed?.Invoke();
        return (list.Count, changed);
    }

    // ---------------------------------------------------------------- the files

    private void Save(bool entries)
    {
        if (entries) dirty = true;
        clientsDirty = true;
        saver.Change(1000, Timeout.Infinite);
    }

    public void Flush()
    {
        lock (gate)
        {
            if (dirty)
            {
                dirty = false;
                JsonFile.Save(file, new Store { Version = Version, Entries = entries.Values.OrderBy(e => Str(e, "name"), StringComparer.Ordinal).ToList() });
            }
            if (clientsDirty)
            {
                clientsDirty = false;
                var since = DateTime.UtcNow - UsageKept;
                foreach (var c in clients.Values)
                    foreach (var k in c.Usage.Where(u => u.Value.At < since).Select(u => u.Key).ToList()) c.Usage.Remove(k);
                JsonFile.Save(clientsFile, new ClientStore { Clients = clients.Values.ToList(), FleetSent = fleetSent });
            }
        }
    }

    public void Dispose()
    {
        saver.Dispose();
        Flush();
    }
}
