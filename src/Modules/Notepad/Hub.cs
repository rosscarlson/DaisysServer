using System.IO;
using System.Text.Json.Nodes;
using DaisysServer.Modules;

namespace DaisysServer.Modules.Notepad;

/// <summary>
/// One user's notes and the clients they have joined from. The messages are Daisy's App's notepad channel (its
/// API.md), with every name, note and change sealed by the client: join, edit, new, close, rename from a client; state,
/// edit, new, close, rename, stale and who from here; plus snap (a client's snapshot of a note at a version, so the
/// changes before it can go) and resync (state again, for a client that lost step). Kept in notes.json in the user's
/// folder, written a second after a change. Callable from any thread.
/// </summary>
internal sealed class Hub
{
    /// <summary>A client's notepad, on its channel.</summary>
    public sealed class Peer(IModuleChannel channel)
    {
        private static int ids;
        public int Id { get; } = Interlocked.Increment(ref ids);
        public string Name => channel.Client;
        public string Cid = "";
        public bool Joined;
        public void Send(JsonObject m) => channel.Post(m);
    }

    /// <summary>A change, sealed, and the version it made.</summary>
    public sealed class Op
    {
        public int V { get; set; }
        public string Parts { get; set; } = "";
        public string By { get; set; } = "";
    }

    /// <summary>A note: its id, its name and text sealed (null: none yet, "Notes n" on the clients), its version, and
    /// the snapshot (at version Sv) with the changes since.</summary>
    public sealed class Note
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public int V { get; set; }
        public int Sv { get; set; }
        public string? Snap { get; set; }
        public List<Op> Ops { get; set; } = new();
        public DateTime Changed { get; set; }

        public JsonObject ToJson()
        {
            var ops = new JsonArray();
            foreach (var o in Ops) ops.Add(new JsonObject { ["v"] = o.V, ["parts"] = o.Parts });
            return new JsonObject { ["id"] = Id, ["name"] = Name, ["v"] = V, ["sv"] = Sv, ["snap"] = Snap, ["ops"] = ops };
        }
    }

    public sealed class Store
    {
        public List<Note> Notes { get; set; } = new();
        public Dictionary<string, int> Seqs { get; set; } = new();
    }

    public sealed record Summary(int Notes, long Bytes, int Changes, DateTime? Changed, IReadOnlyList<string> Clients);

    private readonly object gate = new();
    private readonly ServerUser user;
    private readonly string file;
    private readonly ModuleLog log;
    private readonly Store store;
    private readonly List<Peer> peers = new();
    private readonly Timer saver;
    private bool dirty, forgotten;

    public Hub(ServerUser user, string folder, ModuleLog log)
    {
        this.user = user;
        this.log = log;
        file = Path.Combine(folder, "notes.json");
        store = JsonFile.Load<Store>(file);
        store.Notes = store.Notes.Where(n => n.Id.Length > 0).GroupBy(n => n.Id).Select(g => g.First()).ToList();
        saver = new Timer(_ => Flush());
    }

    private void Changed()
    {
        dirty = true;
        saver.Change(1000, Timeout.Infinite);
    }

    public void Flush()
    {
        lock (gate)
        {
            if (!dirty || forgotten) return;
            dirty = false;
            JsonFile.Save(file, store);
        }
    }

    /// <summary>The user is gone (deleted, or their password reset): nothing more is kept, and their clients are let go.</summary>
    public void Forget()
    {
        lock (gate)
        {
            forgotten = true;
            peers.Clear();
        }
        saver.Dispose();
    }

    public Summary Summarise()
    {
        lock (gate)
        {
            long bytes = store.Notes.Sum(n => (long)(n.Snap?.Length ?? 0) + (n.Name?.Length ?? 0) + n.Ops.Sum(o => o.Parts.Length));
            return new Summary(store.Notes.Count, bytes, store.Notes.Sum(n => n.Ops.Count), store.Notes.Count > 0 ? store.Notes.Max(n => n.Changed) : null,
                peers.Where(p => p.Joined).Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }
    }

    private static string Str(JsonObject m, string name) => m[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    private static string? Sealed(JsonObject m, string name) => Str(m, name) is { Length: > 0 } s ? s : null;

    public void Handle(Peer from, JsonObject m)
    {
        lock (gate)
        {
            if (forgotten) return;
            string op = Str(m, "op");
            if (op == "join") { Join(from, Str(m, "cid")); return; }
            if (!from.Joined) return;
            switch (op)
            {
                case "edit": Edit(from, m); break;
                case "new": Add(m); break;
                case "close": Remove(Str(m, "id")); break;
                case "rename": Rename(Str(m, "id"), Sealed(m, "name")); break;
                case "snap": Snap(m); break;
                case "resync": from.Send(State(from)); break;
            }
        }
    }

    private JsonObject State(Peer p)
    {
        var all = new JsonArray();
        foreach (var n in store.Notes) all.Add(n.ToJson());
        return new JsonObject { ["op"] = "state", ["you"] = p.Id, ["seq"] = store.Seqs.GetValueOrDefault(p.Cid, -1), ["notes"] = all };
    }

    private void Join(Peer p, string cid)
    {
        if (p.Joined) return;
        p.Cid = cid;
        p.Joined = true;
        peers.Add(p);
        p.Send(State(p));
        SendWho();
    }

    public void Leave(Peer p)
    {
        lock (gate)
        {
            if (!peers.Remove(p)) return;
            SendWho();
        }
    }

    private void SendWho()
    {
        var who = new JsonArray();
        foreach (var n in peers.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase)) who.Add(n);
        foreach (var p in peers) p.Send(new JsonObject { ["op"] = "who", ["pcs"] = who.DeepClone() });
    }

    private void SendAll(JsonObject m)
    {
        foreach (var p in peers) p.Send((JsonObject)m.DeepClone());
    }

    /// <summary>{op: "edit", note, base, seq, parts}: applied if it's made on the note's latest version, else stale.</summary>
    private void Edit(Peer from, JsonObject m)
    {
        if (Sealed(m, "parts") is not { } parts) return;
        if (store.Notes.FirstOrDefault(n => n.Id == Str(m, "note")) is not { } note) return;
        if ((int?)m["base"] != note.V)
        {
            from.Send(new JsonObject { ["op"] = "stale" });
            return;
        }
        note.V++;
        note.Ops.Add(new Op { V = note.V, Parts = parts, By = from.Name });
        note.Changed = DateTime.UtcNow;
        if (from.Cid.Length > 0 && (int?)m["seq"] is int seq) store.Seqs[from.Cid] = seq;
        SendAll(new JsonObject { ["op"] = "edit", ["note"] = note.Id, ["v"] = note.V, ["id"] = from.Id, ["by"] = from.Name, ["parts"] = parts });
        Changed();
    }

    /// <summary>{op: "new", id, name, doc}: a note added by a client (the id is its own).</summary>
    private void Add(JsonObject m)
    {
        string id = Str(m, "id");
        if (id.Length is 0 or > 64 || store.Notes.Any(n => n.Id == id)) return;
        var note = new Note { Id = id, Name = Sealed(m, "name"), Snap = Sealed(m, "doc"), Changed = DateTime.UtcNow };
        store.Notes.Add(note);
        var added = note.ToJson();
        added.Remove("sv");
        added.Remove("ops");
        added.Remove("snap");
        added["doc"] = note.Snap;
        added["op"] = "new";
        SendAll(added);
        Changed();
    }

    /// <summary>A note closed; the last one is followed by a new empty one.</summary>
    private void Remove(string id)
    {
        if (store.Notes.RemoveAll(n => n.Id == id) == 0) return;
        SendAll(new JsonObject { ["op"] = "close", ["id"] = id });
        if (store.Notes.Count == 0)
        {
            var note = new Note { Id = Guid.NewGuid().ToString("N")[..12], Changed = DateTime.UtcNow };
            store.Notes.Add(note);
            SendAll(new JsonObject { ["op"] = "new", ["id"] = note.Id, ["name"] = null, ["v"] = 0, ["doc"] = null });
        }
        Changed();
    }

    private void Rename(string id, string? name)
    {
        if (name == null || store.Notes.FirstOrDefault(n => n.Id == id) is not { } note) return;
        note.Name = name;
        note.Changed = DateTime.UtcNow;
        SendAll(new JsonObject { ["op"] = "rename", ["id"] = id, ["name"] = name });
        Changed();
    }

    /// <summary>{op: "snap", note, v, doc}: the note as it was at version v (sealed), so the changes up to it can go.</summary>
    private void Snap(JsonObject m)
    {
        if (store.Notes.FirstOrDefault(n => n.Id == Str(m, "note")) is not { } note || (int?)m["v"] is not int v || Sealed(m, "doc") is not { } doc) return;
        if (v > note.V || v <= note.Sv) return;
        note.Sv = v;
        note.Snap = doc;
        note.Ops.RemoveAll(o => o.V <= v);
        Changed();
    }
}
