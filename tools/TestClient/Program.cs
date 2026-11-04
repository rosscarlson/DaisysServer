// Tries a server end to end: connects with a token, signs in, opens the notepad channel, adds a note and edits it,
// checks that a second client sees both, and that a wrong password and a wrong token are turned away; then the files
// module (if it's there): roots, list, stat, read in pieces, a thumbnail, and paths off the shared drives refused; then the
// processes module (if it's there): full on joining, reports merged and sent to both clients, catching up, the fleet.
// Usage: TestClient <host> <port> <token> <user> <password>
//        TestClient setup <home> <user> <password>   (a test server's users.json and tokens.json in <home>, the
//        DAISYSSERVER_HOME it runs with, and files.json sharing the drive %TEMP% is on; prints the token)
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
    File.WriteAllText(Path.Combine(home, "files.json"), new JsonObject { ["Drives"] = new JsonArray(Path.GetPathRoot(Path.GetTempPath())) }.ToJsonString());
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
if (args is ["report", var rh, var rp, var rt, var ru, var rpw, var rname, var rtitle])
{
    // a third PC on the processes module: reports a reviewed entry (as a person's classification) and prints the version
    var rc = await ServerClient.ConnectAsync(rh, int.Parse(rp), rt, ru, rpw, "TestClient-Report", "TestClient", default);
    var ch = await rc.OpenAsync("processes", "ProcessAnalyzer");
    ch.Post(new JsonObject { ["op"] = "join", ["version"] = 0, ["app"] = "TestClient" });
    ch.Post(new JsonObject { ["op"] = "report", ["entries"] = new JsonArray(new JsonObject
    {
        ["name"] = rname, ["title"] = rtitle, ["publisher"] = "TestClient", ["category"] = "Utility", ["threat"] = "Low",
        ["necessity"] = "Optional", ["resource"] = "Light", ["source"] = "user", ["reviewed"] = true,
        ["updated"] = DateTime.UtcNow.ToString("O"), ["by"] = "TestClient-Report",
    }) });
    using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    int seen = 0;
    while (await ch.ReadAsync(wait.Token) is { } m)
        if (ServerClient.Str(m, "op") == "changes" && ++seen == 2)
        {
            var e = (m["entries"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
            Console.WriteLine($"reported {e?["name"]}: \"{e?["title"]}\", rev {e?["rev"]}, version {m["version"]}");
            break;
        }
    rc.Dispose();
    return 0;
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

if (a.Modules.Contains("files"))
{
    var cf = await a.OpenAsync("files", "files");
    int rid = 0;
    async Task<JsonObject> Ask(JsonObject q)
    {
        int mine = ++rid;
        q["rid"] = mine;
        cf.Post(q);
        using var t = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (await cf.ReadAsync(t.Token) is { } m) if ((int?)m["rid"] == mine) return m;
        throw new Exception("files channel ended: " + cf.Ended);
    }
    var roots = (await Ask(new JsonObject { ["op"] = "roots" }))["roots"] as JsonArray ?? new JsonArray();
    var shared = roots.OfType<JsonObject>().Select(r => ServerClient.Str(r, "path")).ToList();
    Check(roots.Count > 0, "files: roots " + string.Join(", ", roots.OfType<JsonObject>().Select(r => $"{r["name"]} {r["free"]}/{r["total"]}")));
    string dir = Path.Combine(Path.GetTempPath(), "DaisysServerFilesTest");
    Directory.CreateDirectory(Path.Combine(dir, "sub"));
    var bytes = new byte[300_000];
    new Random(7).NextBytes(bytes);
    File.WriteAllBytes(Path.Combine(dir, "data10.bin"), bytes);
    File.WriteAllBytes(Path.Combine(dir, "data2.bin"), [1, 2, 3]);
    File.WriteAllText(Path.Combine(dir, "hello.txt"), "hello");
    var list = await Ask(new JsonObject { ["op"] = "list", ["path"] = dir });
    var names = (list["items"] as JsonArray)?.OfType<JsonObject>().Select(i => ServerClient.Str(i, "name")).ToList() ?? [];
    Check(names.SequenceEqual(["sub", "data2.bin", "data10.bin", "hello.txt"]), "files: list, folders first in Explorer's order: " + string.Join(", ", names) + " " + list["message"]);
    var paged = await Ask(new JsonObject { ["op"] = "list", ["path"] = dir, ["skip"] = 1, ["max"] = 2 });
    Check((int?)paged["count"] == 4 && (paged["items"] as JsonArray)?.Count == 2, "files: list in pages (count 4, 2 from 1)");
    var stat = await Ask(new JsonObject { ["op"] = "stat", ["path"] = Path.Combine(dir, "hello.txt") });
    Check((long?)stat["size"] == 5 && ServerClient.Str(stat, "mime") == "text/plain" && ServerClient.Str(stat, "ext") == ".txt", $"files: stat {stat.ToJsonString()[..Math.Min(160, stat.ToJsonString().Length)]}");
    var got = new MemoryStream();
    long offset = 0;
    bool eof = false;
    int pieces = 0;
    while (!eof && pieces < 10)
    {
        var r = await Ask(new JsonObject { ["op"] = "read", ["path"] = Path.Combine(dir, "data10.bin"), ["offset"] = offset, ["length"] = 128 * 1024 });
        var piece = Convert.FromBase64String(ServerClient.Str(r, "data"));
        got.Write(piece);
        offset += piece.Length;
        eof = (bool?)r["eof"] == true;
        pieces++;
    }
    Check(eof && pieces == 3 && got.ToArray().SequenceEqual(bytes), $"files: read 300 KB in {pieces} pieces, the same bytes");
    var past = await Ask(new JsonObject { ["op"] = "read", ["path"] = Path.Combine(dir, "hello.txt"), ["offset"] = 99 });
    Check((int?)past["length"] == 0 && (bool?)past["eof"] == true, "files: a read past the end is empty, eof");
    var folder = await Ask(new JsonObject { ["op"] = "read", ["path"] = dir });
    Check(ServerClient.Str(folder, "op") == "error", "files: reading a folder: " + folder["message"]);
    var up = await Ask(new JsonObject { ["op"] = "stat", ["path"] = Path.Combine(dir, "..", "DaisysServerFilesTest", "sub") });
    Check((bool?)up["dir"] == true, "files: .. in a path is fine on the same drive");
    string other = DriveInfo.GetDrives().Select(d => d.Name).FirstOrDefault(n => !shared.Contains(n, StringComparer.OrdinalIgnoreCase)) ?? "Q:\\";
    var off = await Ask(new JsonObject { ["op"] = "list", ["path"] = other });
    Check(ServerClient.Str(off, "op") == "error", $"files: {other} (not shared) refused: {off["message"]}");
    foreach (var bad in new[] { @"\\localhost\c$", Path.Combine(dir, "hello.txt") + ":stream", @"relative\path", dir + @"\..\..\..\..\..\..\..\..\" + other })
    {
        var r = await Ask(new JsonObject { ["op"] = "list", ["path"] = bad });
        Check(ServerClient.Str(r, "op") == "error", $"files: {bad} refused: {r["message"]}");
    }
    string link = Path.Combine(dir, "link");
    try
    {
        if (!Directory.Exists(link) && other.Length == 3 && Directory.Exists(other)) Directory.CreateSymbolicLink(link, other);
    }
    catch
    {
        // (a symbolic link needs admin: a junction doesn't)
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{other}\"") { UseShellExecute = false, RedirectStandardOutput = true })?.WaitForExit();
    }
    if (Directory.Exists(link))
    {
        var r = await Ask(new JsonObject { ["op"] = "list", ["path"] = link });
        Check(ServerClient.Str(r, "op") == "error", $"files: a link to {other} refused: {r["message"]}");
    }
    string wallpaper = @"C:\Windows\Web\Wallpaper\Windows\img0.jpg";
    if (File.Exists(wallpaper) && shared.Contains("C:\\", StringComparer.OrdinalIgnoreCase))
    {
        var th = await Ask(new JsonObject { ["op"] = "thumb", ["path"] = wallpaper, ["size"] = 200 });
        Check(ServerClient.Str(th, "data").Length > 1000 && (int?)th["w"] <= 200, $"files: a thumbnail, {th["type"]} {th["w"]}x{th["h"]}, {ServerClient.Str(th, "data").Length} base64");
    }
    var none = await Ask(new JsonObject { ["op"] = "thumb", ["path"] = Path.Combine(dir, "data2.bin") });
    Check(ServerClient.Str(none, "op") == "thumb" && none["data"] == null, "files: no thumbnail for a .bin: data null");
    try { Directory.Delete(link); } catch { }
    try { Directory.Delete(dir, true); } catch { }
}

if (a.Modules.Contains("processes"))
{
    // two PCs on the master database: a report reaches both, reviewed beats automatic, catching up, the fleet's figures
    string x = "testproc" + Guid.NewGuid().ToString("N")[..6];
    JsonObject Entry(bool reviewed, DateTime updated, string title, JsonObject? extra = null)
    {
        var e = new JsonObject
        {
            ["name"] = x + ".EXE", ["title"] = title, ["publisher"] = "Test", ["category"] = "Utility", ["threat"] = "Safe",
            ["necessity"] = "Optional", ["resource"] = "Light", ["source"] = reviewed ? "user" : "auto", ["reviewed"] = reviewed,
            ["updated"] = updated.ToString("O"), ["by"] = "TestClient", ["rev"] = 999999,
        };
        foreach (var (k, v) in extra ?? new JsonObject()) e[k] = v?.DeepClone();
        return e;
    }
    JsonObject? Of(JsonObject changes) => (changes["entries"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(e => ServerClient.Str(e, "name") == x);
    async Task<ServerChannel> Join(ServerClient sc, long version)
    {
        var ch = await sc.OpenAsync("processes", "ProcessAnalyzer");
        ch.Post(new JsonObject { ["op"] = "join", ["version"] = version, ["app"] = "TestClient" });
        return ch;
    }
    var pa = await Join(a, 0);
    var first = await Next(pa, "changes");
    long v0 = (long)first["version"]!;
    Check((bool?)first["full"] == true, $"processes: a new client gets everything (full, version {v0}, {(first["entries"] as JsonArray)?.Count} entries)");
    var clientsMsg = await Next(pa, "clients");
    Check(clientsMsg["clients"] is JsonArray, "processes: clients after join");
    pa.Post(new JsonObject { ["op"] = "seen", ["version"] = v0 });
    var pb = await Join(b, v0);
    var bFirst = await Next(pb, "changes");
    Check(v0 == 0 ? (bool?)bFirst["full"] == true : (bool?)bFirst["full"] == false && (bFirst["entries"] as JsonArray)?.Count == 0,
        v0 == 0 ? "processes: version 0 always gets everything" : "processes: a client up to date gets nothing (full false)");
    await Next(pb, "clients");

    var t0 = DateTime.UtcNow.AddMinutes(-10);
    pa.Post(new JsonObject { ["op"] = "report", ["entries"] = new JsonArray(Entry(false, t0, "Auto", new JsonObject { ["futureField"] = 7, ["fleetCpu"] = 99.0 })) });
    var ackA = await Next(pa, "changes");
    var gotB = await Next(pb, "changes");
    var ea = Of(ackA);
    Check((long?)ackA["version"] == v0 + 1 && (long?)ea?["rev"] == v0 + 1 && Of(gotB) != null, $"processes: a report is numbered (rev {ea?["rev"]}) and reaches the other client");
    Check((int?)ea?["futureField"] == 7 && ea?["fleetCpu"] == null && ServerClient.Str(ea!, "name") == x, "processes: unknown fields kept, a client's fleet figures dropped, the name keyed");

    pa.Post(new JsonObject { ["op"] = "report", ["entries"] = new JsonArray(Entry(false, t0, "Same time")) });
    var tie = await Next(pa, "changes");
    Check((long?)tie["version"] == v0 + 1 && ServerClient.Str(Of(tie)!, "title") == "Auto", "processes: a tie keeps the stored one, answered with it (no new version)");

    pb.Post(new JsonObject { ["op"] = "report", ["entries"] = new JsonArray(Entry(true, t0.AddMinutes(1), "Reviewed")) });
    await Next(pb, "changes");
    var revA = await Next(pa, "changes");
    Check(ServerClient.Str(Of(revA)!, "title") == "Reviewed" && (long?)revA["version"] == v0 + 2, "processes: a reviewed entry replaces an automatic one");
    pa.Post(new JsonObject { ["op"] = "report", ["entries"] = new JsonArray(Entry(false, DateTime.UtcNow, "Newer auto")) });
    var keep = await Next(pa, "changes");
    Check(ServerClient.Str(Of(keep)!, "title") == "Reviewed" && (long?)keep["version"] == v0 + 2, "processes: a newer automatic one doesn't replace a reviewed one");
    pa.Post(new JsonObject { ["op"] = "seen", ["version"] = v0 + 2 });

    var pc = await Join(a, v0 + 1);
    var catchUp = await Next(pc, "changes");
    Check((bool?)catchUp["full"] == false && (catchUp["entries"] as JsonArray)!.Count >= 1 && ServerClient.Str(Of(catchUp)!, "title") == "Reviewed",
        $"processes: a client behind catches up ({(catchUp["entries"] as JsonArray)!.Count} since {v0 + 1})");
    var pcClients = await Next(pc, "clients");
    Check((pcClients["clients"] as JsonArray)!.OfType<JsonObject>().Any(k => ServerClient.Str(k, "name") == "TestClient-A" && (long?)k["version"] == v0 + 2),
        "processes: seen kept (TestClient-A at the latest)");
    pc.Dispose();
    var other = await Join(a, v0 + 1000);
    Check((bool?)(await Next(other, "changes"))["full"] == true, "processes: a version past the server's (another server's) gets everything");
    other.Dispose();

    JsonObject Usage(double cpu, double mem) => new() { ["op"] = "usage", ["items"] = new JsonArray(new JsonObject
        { ["name"] = x, ["cpu"] = cpu, ["peakCpu"] = cpu * 3, ["memoryMB"] = mem, ["peakMemoryMB"] = mem * 2, ["samples"] = 50 }) };
    pa.Post(Usage(2, 100));
    pb.Post(Usage(4, 300));
    await Task.Delay(500);
    var fleet = await Join(a, 0);
    var fe = Of(await Next(fleet, "changes"));
    Check((double?)fe?["fleetCpu"] == 3 && (double?)fe?["fleetMemoryMB"] == 200 && (int?)fe?["fleetPcs"] == 2,
        $"processes: the fleet's figures averaged over the PCs (cpu {fe?["fleetCpu"]}, memory {fe?["fleetMemoryMB"]}, PCs {fe?["fleetPcs"]})");
    fleet.Dispose();
    pa.Dispose();
    pb.Dispose();
}

a.Dispose();
b.Dispose();
Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
return failures == 0 ? 0 : 1;
