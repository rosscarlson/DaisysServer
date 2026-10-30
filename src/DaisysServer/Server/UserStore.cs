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

        public ServerUser ToServerUser() => new(Id, Name, Admin);
        public User Copy() => (User)MemberwiseClone();
    }

    private sealed class FileShape { public List<User> Users { get; set; } = new(); }

    private static string FilePath => Path.Combine(JsonFile.SettingsFolder, "users.json");
    private readonly object gate = new();
    private readonly List<User> users;
    private readonly byte[] secret;

    /// <summary>A user added, removed or changed (any thread).</summary>
    public event Action? Changed;
    /// <summary>A user deleted, or their password reset by an admin (their data goes).</summary>
    public event Action<ServerUser>? Removed;

    public UserStore(byte[] secret)
    {
        this.secret = secret;
        users = JsonFile.Load<FileShape>(FilePath).Users;
    }

    public List<User> All() { lock (gate) return users.Select(u => u.Copy()).ToList(); }
    public bool Any() { lock (gate) return users.Count > 0; }

    private User? Find(string name) => users.FirstOrDefault(u => u.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
    private void Save() => JsonFile.Save(FilePath, new FileShape { Users = users });

    public static string? Problem(string name, string password)
    {
        name = name.Trim();
        if (name.Length is 0 or > 40) return "A name is 1 to 40 characters.";
        if (name.Any(char.IsControl)) return "That name has characters it can't.";
        if (password.Length < 8) return "A password is at least 8 characters.";
        return null;
    }

    /// <summary>Adds a user (the password made into the verifier here, then forgotten). Returns a problem, or null.</summary>
    public string? Add(string name, string password, bool admin)
    {
        if (Problem(name, password) is { } p) return p;
        var u = new User { Id = Guid.NewGuid().ToString("N")[..12], Name = name.Trim(), Admin = admin, Created = DateTime.UtcNow };
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
        Changed?.Invoke();
        return null;
    }

    public string? SetAdmin(string id, bool admin)
    {
        lock (gate)
        {
            var u = users.FirstOrDefault(x => x.Id == id);
            if (u == null) return "No such user.";
            if (!admin && u.Admin && users.Count(x => x.Admin) == 1) return "That's the last admin.";
            u.Admin = admin;
            Save();
            ModuleLog.Server.Info($"User {u.Name}: {(admin ? "admin" : "basic")}");
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
