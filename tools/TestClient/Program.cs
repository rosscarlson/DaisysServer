// Tries a server end to end: connects with a token, signs in, opens the notepad channel, adds a note and edits it,
// checks that a second client sees both, and that a wrong password and a wrong token are turned away.
// Usage: TestClient <host> <port> <token> <user> <password>
//        TestClient setup <home> <user> <password>   (a test server's users.json and tokens.json in <home>, the
//        DAISYSSERVER_HOME it runs with; prints the token)
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using DaisysServer.Protocol;

if (args is ["setup", var home, var newUser, var newPassword])
{
    Directory.CreateDirectory(home);
    var salt = RandomNumberGenerator.GetBytes(16);
    var vsalt = RandomNumberGenerator.GetBytes(16);
    var auth = Vault.Auth(Vault.Master(newPassword, salt, Vault.Iterations));
    var users = new JsonObject { ["Users"] = new JsonArray(new JsonObject
    {
        ["Id"] = "test" + Guid.NewGuid().ToString("N")[..8], ["Name"] = newUser, ["Admin"] = true, ["Salt"] = Convert.ToBase64String(salt),
        ["Iterations"] = Vault.Iterations, ["VerifierSalt"] = Convert.ToBase64String(vsalt),
        ["Verifier"] = Convert.ToBase64String(Vault.Verifier(auth, vsalt)), ["Created"] = DateTime.UtcNow,
    }) };
    File.WriteAllText(Path.Combine(home, "users.json"), users.ToJsonString());
    string made = Token.Make(out var tid, out var secret);
    var tokens = new JsonObject { ["Tokens"] = new JsonArray(new JsonObject
    {
        ["Id"] = tid, ["Name"] = "Test", ["Created"] = DateTime.UtcNow,
        ["Secret"] = Convert.ToBase64String(ProtectedData.Protect(secret, null, DataProtectionScope.CurrentUser)),
    }) };
    File.WriteAllText(Path.Combine(home, "tokens.json"), tokens.ToJsonString());
    Console.WriteLine(made);
    return 0;
}

if (args is ["poke", var ph, var pp, var pt, var pu, var ppw, var find, var append, var rename])
{
    // a third client: appends text to the note whose name has <find> in it, and renames it
    var pc = await ServerClient.ConnectAsync(ph, int.Parse(pp), pt, pu, ppw, "TestClient-Poke", "TestClient", default);
    var ch = await pc.OpenAsync("notepad", "notepad");
    ch.Post(new JsonObject { ["op"] = "join", ["cid"] = "poke" });
    using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    JsonObject? st = null;
    while (await ch.ReadAsync(wait.Token) is { } m0) if (ServerClient.Str(m0, "op") == "state") { st = m0; break; }
    foreach (var n in (st!["notes"] as JsonArray)!.OfType<JsonObject>())
    {
        string nm = ServerClient.Str(n, "name") is { Length: > 0 } sn ? Vault.OpenText(pc.DataKey, sn) : "";
        if (!nm.Contains(find)) continue;
        // its length: the snapshot's characters, then each change's (kept + put in)
        int Length(JsonNode? runs) => (runs as JsonArray)!.OfType<JsonObject>().Sum(r => ((string?)r["t"] ?? "").Length);
        int len = ServerClient.Str(n, "snap") is { Length: > 0 } snap ? Length(JsonNode.Parse(Vault.OpenText(pc.DataKey, snap))) : 0;
        foreach (var op in (n["ops"] as JsonArray)!.OfType<JsonObject>())
            len = (JsonNode.Parse(Vault.OpenText(pc.DataKey, ServerClient.Str(op, "parts"))) as JsonArray)!.OfType<JsonObject>()
                .Sum(x => (int?)x["k"] ?? (x["i"] is JsonNode ins ? Length(ins) : 0));
        string parts = new JsonArray(new JsonObject { ["k"] = len }, new JsonObject { ["i"] = new JsonArray(new JsonObject { ["t"] = append, ["a"] = "Poke", ["f"] = 1 }) }).ToJsonString();
        ch.Post(new JsonObject { ["op"] = "edit", ["note"] = ServerClient.Str(n, "id"), ["base"] = (int)n["v"]!, ["seq"] = 1, ["parts"] = Vault.SealText(pc.DataKey, parts) });
        ch.Post(new JsonObject { ["op"] = "rename", ["id"] = ServerClient.Str(n, "id"), ["name"] = Vault.SealText(pc.DataKey, rename) });
        while (await ch.ReadAsync(wait.Token) is { } m1) if (ServerClient.Str(m1, "op") == "rename") break;
        Console.WriteLine($"poked {nm} (length {len}, version {n["v"]})");
        pc.Dispose();
        return 0;
    }
    Console.WriteLine("no note named like " + find);
    return 1;
}
if (args.Length < 5) { Console.WriteLine("TestClient <host> <port> <token> <user> <password>"); return 2; }
string host = args[0], token = args[2], user = args[3], password = args[4];
int port = int.Parse(args[1]);
int failures = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) failures++; }

async Task<JsonObject> Next(ServerChannel c, string op)
{
    using var t = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    while (await c.ReadAsync(t.Token) is { } m) if (ServerClient.Str(m, "op") == op) return m;
    throw new Exception($"channel ended before {op}: {c.Ended}");
}

var a = await ServerClient.ConnectAsync(host, port, token, user, password, "TestClient-A", "TestClient", default);
Check(a.DataKey.Length == 32, $"signed in as {a.User} on {a.Server} {a.ServerVersion}, modules: {string.Join(",", a.Modules)}");
var ca = await a.OpenAsync("notepad", "notepad");
ca.Post(new JsonObject { ["op"] = "join", ["cid"] = "test-a" });
var state = await Next(ca, "state");
Check(state["notes"] is JsonArray, $"state with {(state["notes"] as JsonArray)?.Count} notes");

string id = Guid.NewGuid().ToString("N")[..12];
string name = "Test " + DateTime.Now.ToString("HHmmss");
ca.Post(new JsonObject { ["op"] = "new", ["id"] = id, ["name"] = Vault.SealText(a.DataKey, name), ["doc"] = Vault.SealText(a.DataKey, "[{\"t\":\"hello\",\"a\":\"A\"}]") });
var added = await Next(ca, "new");
Check(ServerClient.Str(added, "id") == id && Vault.OpenText(a.DataKey, ServerClient.Str(added, "name")) == name, "new note echoed, its name opens");
ca.Post(new JsonObject { ["op"] = "edit", ["note"] = id, ["base"] = 0, ["seq"] = 1, ["parts"] = Vault.SealText(a.DataKey, "[{\"k\":5},{\"i\":[{\"t\":\" world\",\"a\":\"A\"}]}]") });
var edit = await Next(ca, "edit");
Check((int?)edit["v"] == 1, "edit applied as version 1");
ca.Post(new JsonObject { ["op"] = "edit", ["note"] = id, ["base"] = 0, ["seq"] = 2, ["parts"] = Vault.SealText(a.DataKey, "[]") });
await Next(ca, "stale");
Check(true, "an edit on an old version is stale");

var b = await ServerClient.ConnectAsync(host, port, token, user, password, "TestClient-B", "TestClient", default);
Check(b.DataKey.SequenceEqual(a.DataKey), "a second client gets the same data key");
var cb = await b.OpenAsync("notepad", "notepad");
cb.Post(new JsonObject { ["op"] = "join", ["cid"] = "test-b" });
var stateB = await Next(cb, "state");
var note = (stateB["notes"] as JsonArray)!.OfType<JsonObject>().First(n => ServerClient.Str(n, "id") == id);
Check((int?)note["v"] == 1 && (note["ops"] as JsonArray)!.Count == 1, "the second client sees the note at version 1 with its change");
Check(Vault.OpenText(b.DataKey, ServerClient.Str(note, "snap")).Contains("hello"), "and opens its text");
var who = await Next(ca, "who");
var pcs = (who["pcs"] as JsonArray)!.Select(x => (string?)x).ToList();
Check(pcs.Contains("TestClient-A") && pcs.Contains("TestClient-B"), "who: both clients");
cb.Post(new JsonObject { ["op"] = "rename", ["id"] = id, ["name"] = Vault.SealText(b.DataKey, "Renamed") });
var ren = await Next(ca, "rename");
Check(Vault.OpenText(a.DataKey, ServerClient.Str(ren, "name")) == "Renamed", "a rename on one client reaches the other");
cb.Post(new JsonObject { ["op"] = "snap", ["note"] = id, ["v"] = 1, ["doc"] = Vault.SealText(b.DataKey, "[{\"t\":\"hello world\",\"a\":\"A\"}]") });
cb.Post(new JsonObject { ["op"] = "close", ["id"] = id });
await Next(ca, "close");
Check(true, "close reaches the other client");

try { await a.OpenAsync("nosuchmodule", "x"); Check(false, "unknown module refused"); }
catch (ServerException ex) { Check(true, "unknown module refused: " + ex.Message); }
try { await ServerClient.ConnectAsync(host, port, token, user, password + "x", "TestClient-C", "TestClient", default); Check(false, "wrong password refused"); }
catch (ServerException ex) { Check(true, "wrong password refused: " + ex.Message); }
try
{
    var bad = Token.Make(out _, out _);
    await ServerClient.ConnectAsync(host, port, bad, user, password, "TestClient-D", "TestClient", default);
    Check(false, "unknown token refused");
}
catch (ServerException ex) { Check(true, "unknown token refused: " + ex.Message); }

await b.ChangePasswordAsync(password, password + "-new");
var c2 = await ServerClient.ConnectAsync(host, port, token, user, password + "-new", "TestClient-E", "TestClient", default);
Check(c2.DataKey.SequenceEqual(a.DataKey), "after a password change, the new one signs in and opens the same data key");
await c2.ChangePasswordAsync(password + "-new", password);
c2.Dispose();
try { await b.ChangePasswordAsync("not it", "whatever123"); Check(false, "a wrong current password can't change it"); }
catch (ServerException ex) { Check(true, "a wrong current password can't change it: " + ex.Message); }

a.Dispose();
b.Dispose();
Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
