using System.IO;
using System.Security.Cryptography;
using System.Text;
using DaisysServer.Modules;
using DaisysServer.Protocol;

namespace DaisysServer.Server;

/// <summary>
/// The users (%APPDATA%\DaisysServer\users.json). For each: their salt and rounds (handed to a client to make the
/// keys from the password), a verifier of the sign-in proof (never the password, nor the proof itself), and their data
/// key wrapped by their client (which the server can't unwrap). Admins can manage users from a client too.
/// </summary>
public sealed class UserStore
{
    public sealed class User
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Admin { get; set; }
        public string Salt { get; set; } = "";
        public int Iterations { get; set; } = Protocol.Vault.Iterations;
        public string VerifierSalt { get; set; } = "";
        public string Verifier { get; set; } = "";
        /// <summary>The data key, wrapped on the client with a key from the password; null until the first sign-in.</summary>
        public string? Vault { get; set; }
        public DateTime Created { get; set; }
        public DateTime? LastSignIn { get; set; }
        /// <summary>The profile: a value for each of <see cref="Fields"/>' keys (missing ones are empty).</summary>
        public Dictionary<string, string> Profile { get; set; } = new();
        /// <summary>Their groups, from <see cref="Groups"/> (Admin among them when <see cref="Admin"/>).</summary>
        public List<string> Groups { get; set; } = new();

        public string Get(string key) => Profile.TryGetValue(key, out var v) ? v : "";
        public string DisplayName => Get("displayName") is { Length: > 0 } d ? d : Name;
        public ServerUser ToServerUser() => new(Id, Name, Admin);
        public override string ToString() => Name;

        public User Copy()
        {
            var c = (User)MemberwiseClone();
            c.Profile = new Dictionary<string, string>(Profile);
            c.Groups = new List<string>(Groups);
            return c;
        }
    }

    /// <summary>A profile field: its key (in users.json and on the wire), its label, a tooltip, its longest, and what's
    /// wrong with a value (null when it's fine; empty is always fine).</summary>
    public sealed record Field(string Key, string Label, string Tip, int Max, Func<string, string?>? Check = null);

    /// <summary>The profile fields, in the order they're shown. A new one is a line here: the Users page, users.json
    /// and the <c>users</c>/<c>useredit</c> ops all go by this list.</summary>
    public static readonly IReadOnlyList<Field> Fields =
    [
        new("displayName", "Display name", "What others see; the user name if empty", 60),
        new("email", "Email", "To reach them", 254, v => System.Net.Mail.MailAddress.TryCreate(v, out var a) && a.Address == v ? null : "That isn't an email address."),
    ];

    /// <summary>The groups a user can be in, in the order they're shown. "Admin" is <see cref="User.Admin"/>; a new
    /// user is in "User".</summary>
    public static readonly IReadOnlyList<string> Groups = ["Admin", "User", "SE-Analysis"];
    public const string AdminGroup = "Admin", UserGroup = "User";

    private sealed class FileShape { public List<User> Users { get; set; } = new(); }

    private static string FilePath => Path.Combine(JsonFile.SettingsFolder, "users.json");
    private readonly object gate = new();
    private readonly List<User> users;
    private readonly byte[] secret;

    /// <summary>A user added, removed or changed (any thread).</summary>
    public event Action? Changed;
    /// <summary>A user deleted, or their password reset by an admin (their data goes).</summary>
    public event Action<ServerUser>? Removed;
    /// <summary>A user deleted (after <see cref="Removed"/>): their tokens go.</summary>
    public event Action<ServerUser>? Deleted;

    public UserStore(byte[] secret)
    {
        this.secret = secret;
        users = JsonFile.Load<FileShape>(FilePath).Users;
        // users from before 0.2.3 had no groups
        foreach (var u in users.Where(u => u.Groups.Count == 0)) u.Groups = Sorted(u.Admin ? new[] { AdminGroup, UserGroup } : new[] { UserGroup });
    }

    /// <summary>The known groups among these, in <see cref="Groups"/>' order.</summary>
    private static List<string> Sorted(IEnumerable<string> groups)
    {
        var set = groups.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Groups.Where(set.Contains).ToList();
    }

    public List<User> All() { lock (gate) return users.Select(u => u.Copy()).ToList(); }
    public bool Any() { lock (gate) return users.Count > 0; }

    private User? Find(string name) => users.FirstOrDefault(u => u.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
    private void Save() => JsonFile.Save(FilePath, new FileShape { Users = users });

    public static string? Problem(string name, string password) =>
        NameProblem(name) ?? (password.Length < 8 ? "A password is at least 8 characters." : null);

    private static string? NameProblem(string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 40) return "A name is 1 to 40 characters.";
        if (name.Any(char.IsControl)) return "That name has characters it can't.";
        return null;
    }

    /// <summary>Adds a user (the password made into the verifier here, then forgotten). Returns a problem, or null.</summary>
    public string? Add(string name, string password, bool admin, IReadOnlyDictionary<string, string>? profile = null, IEnumerable<string>? groups = null)
    {
        // their groups, if given, say whether they're an admin
        var g = Sorted(groups ?? (admin ? new[] { AdminGroup, UserGroup } : new[] { UserGroup }));
        admin = g.Contains(AdminGroup);
        if (Problem(name, password) is { } p) return p;
        var (clean, problem) = Clean(profile);
        if (problem != null) return problem;
        var u = new User { Id = Guid.NewGuid().ToString("N")[..12], Name = name.Trim(), Admin = admin, Groups = g, Created = DateTime.UtcNow };
        Apply(u, clean);
        SetPassword(u, password);
        lock (gate)
        {
            if (Find(name) != null) return "There's a user with that name already.";
            users.Add(u);
            Save();
        }
        ModuleLog.Server.Info($"User {u.Name} added{(admin ? " (admin)" : "")}");
        Changed?.Invoke();
        return null;
    }

    private static void SetPassword(User u, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var vsalt = RandomNumberGenerator.GetBytes(16);
        var auth = Vault.Auth(Vault.Master(password, salt, Vault.Iterations));
        u.Salt = Convert.ToBase64String(salt);
        u.Iterations = Vault.Iterations;
        u.VerifierSalt = Convert.ToBase64String(vsalt);
        u.Verifier = Convert.ToBase64String(Vault.Verifier(auth, vsalt));
        u.Vault = null;
    }

    /// <summary>A new password set by an admin: their data key can't be unwrapped any more, so their data goes.</summary>
    public string? ResetPassword(string id, string password)
    {
        if (password.Length < 8) return "A password is at least 8 characters.";
        User? u;
        lock (gate)
        {
            u = users.FirstOrDefault(x => x.Id == id);
            if (u == null) return "No such user.";
            SetPassword(u, password);
            Save();
        }
        ModuleLog.Server.Info($"User {u.Name}: password reset (their data was deleted)");
        Removed?.Invoke(u.ToServerUser());
        Changed?.Invoke();
        return null;
    }

    public string? Delete(string id)
    {
        User? u;
        lock (gate)
        {
            u = users.FirstOrDefault(x => x.Id == id);
            if (u == null) return "No such user.";
            if (u.Admin && users.Count(x => x.Admin) == 1 && users.Count > 1) return "That's the last admin.";
            users.Remove(u);
            Save();
        }
        ModuleLog.Server.Info($"User {u.Name} deleted");
        Removed?.Invoke(u.ToServerUser());
        Deleted?.Invoke(u.ToServerUser());
        Changed?.Invoke();
        return null;
    }

    /// <summary>A user's name and profile changed (only the keys given; unknown ones ignored). Their password and data
    /// stay: the keys come from the password and a random salt, not the name.</summary>
    public string? Edit(string id, string? name, IReadOnlyDictionary<string, string> profile)
    {
        var (clean, problem) = Clean(profile);
        if (problem != null) return problem;
        User? u;
        lock (gate)
        {
            u = users.FirstOrDefault(x => x.Id == id);
            if (u == null) return "No such user.";
            if (name != null && name.Trim() != u.Name)
            {
                if (NameProblem(name) is { } p) return p;
                if (Find(name) is { } other && other != u) return "There's a user with that name already.";
                ModuleLog.Server.Info($"User {u.Name} renamed {name.Trim()}");
                u.Name = name.Trim();
            }
            Apply(u, clean);
            Save();
        }
        Changed?.Invoke();
        return null;
    }

    /// <summary>The profile's values for the known fields, trimmed, or what's wrong with one.</summary>
    private static (Dictionary<string, string> Clean, string? Problem) Clean(IReadOnlyDictionary<string, string>? profile)
    {
        var clean = new Dictionary<string, string>();
        foreach (var f in Fields)
        {
            if (profile == null || !profile.TryGetValue(f.Key, out var v)) continue;
            v = v.Trim();
            if (v.Length > f.Max) return (clean, $"{f.Label} is at most {f.Max} characters.");
            if (v.Any(char.IsControl)) return (clean, $"{f.Label} has characters it can't.");
            if (v.Length > 0 && f.Check?.Invoke(v) is { } p) return (clean, p);
            clean[f.Key] = v;
        }
        return (clean, null);
    }

    private static void Apply(User u, Dictionary<string, string> clean)
    {
        foreach (var (k, v) in clean)
            if (v.Length > 0) u.Profile[k] = v;
            else u.Profile.Remove(k);
    }

    public string? SetAdmin(string id, bool admin)
    {
        List<string> g;
        lock (gate)
        {
            if (users.FirstOrDefault(x => x.Id == id) is not { } u) return "No such user.";
            g = u.Groups.ToList();
        }
        return SetGroups(id, admin ? g.Append(AdminGroup) : g.Where(x => x != AdminGroup));
    }

    /// <summary>A user's groups (unknown ones ignored); being in Admin makes them an admin.</summary>
    public string? SetGroups(string id, IEnumerable<string> groups)
    {
        lock (gate)
        {
            var u = users.FirstOrDefault(x => x.Id == id);
            if (u == null) return "No such user.";
            var g = Sorted(groups);
            bool admin = g.Contains(AdminGroup);
            if (!admin && u.Admin && users.Count(x => x.Admin) == 1) return "That's the last admin.";
            if (g.SequenceEqual(u.Groups) && admin == u.Admin) return null;
            u.Groups = g;
            u.Admin = admin;
            Save();
            ModuleLog.Server.Info($"User {u.Name}: {(g.Count > 0 ? string.Join(", ", g) : "no groups")}");
        }
        Changed?.Invoke();
        return null;
    }

    /// <summary>A user's salt and rounds; for a name there's no user of, made-up ones that stay the same (so a client
    /// can't tell which names there are).</summary>
    public (string Salt, int Iterations) Prelogin(string name)
    {
        lock (gate)
            if (Find(name) is { } u) return (u.Salt, u.Iterations);
        var fake = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(name.Trim().ToLowerInvariant()))[..16];
        return (Convert.ToBase64String(fake), Vault.Iterations);
    }

    /// <summary>The user, if the proof is theirs.</summary>
    public User? SignIn(string name, string authBase64)
    {
        lock (gate)
        {
            if (Find(name) is not { } u || !Check(u, authBase64)) return null;
            u.LastSignIn = DateTime.UtcNow;
            Save();
            return u.Copy();
        }
    }

    private static bool Check(User u, string authBase64)
    {
        try
        {
            var check = Vault.Verifier(Convert.FromBase64String(authBase64), Convert.FromBase64String(u.VerifierSalt));
            return CryptographicOperations.FixedTimeEquals(check, Convert.FromBase64String(u.Verifier));
        }
        catch (FormatException) { return false; }
    }

    /// <summary>The first data key a user's client offers is kept; the one kept is returned.</summary>
    public string? SetVault(string id, string vault)
    {
        lock (gate)
        {
            if (users.FirstOrDefault(x => x.Id == id) is not { } u) return null;
            if (u.Vault == null)
            {
                u.Vault = vault;
                Save();
            }
            return u.Vault;
        }
    }

    /// <summary>The password changed by the user (on a client): the old proof checked, the new salt, proof and wrapped key kept.</summary>
    public bool ChangePassword(string id, string oldAuth, string salt, int iterations, string newAuth, string vault)
    {
        lock (gate)
        {
            if (users.FirstOrDefault(x => x.Id == id) is not { } u || !Check(u, oldAuth)) return false;
            var vsalt = RandomNumberGenerator.GetBytes(16);
            u.Salt = salt;
            u.Iterations = Math.Clamp(iterations, 100_000, 5_000_000);
            u.VerifierSalt = Convert.ToBase64String(vsalt);
            u.Verifier = Convert.ToBase64String(Vault.Verifier(Convert.FromBase64String(newAuth), vsalt));
            u.Vault = vault;
            Save();
            ModuleLog.Server.Info($"User {u.Name} changed their password");
            return true;
        }
    }
}
