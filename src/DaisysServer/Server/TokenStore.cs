using System.IO;
using System.Security.Cryptography;
using DaisysServer.Modules;
using DaisysServer.Protocol;

namespace DaisysServer.Server;

/// <summary>
/// The connection tokens (%APPDATA%\DaisysServer\tokens.json): each a name, an id and a secret (kept encrypted for this
/// Windows user, DPAPI) and, since 0.2.2, the user it's for (only they can sign in with it; tokens made before are for
/// anyone until they're tied to someone). A client needs one to connect at all; revoking one stops new connections with
/// it, and drops the ones using it. Made, copied and revoked on the Users page.
/// </summary>
public sealed class TokenStore
{
    public sealed class Entry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>The secret, DPAPI-protected (CurrentUser), base64.</summary>
        public string Secret { get; set; } = "";
        public DateTime Created { get; set; }
        public DateTime? LastUsed { get; set; }
        /// <summary>The user it's for; empty: anyone (a token made before 0.2.2).</summary>
        public string UserId { get; set; } = "";
        public Entry Copy() => (Entry)MemberwiseClone();
    }

    private sealed class FileShape { public List<Entry> Tokens { get; set; } = new(); }

    private static string FilePath => Path.Combine(JsonFile.SettingsFolder, "tokens.json");
    private readonly object gate = new();
    private readonly List<Entry> tokens;

    public event Action? Changed;
    /// <summary>A token revoked: its connections are dropped.</summary>
    public event Action<string>? Revoked;

    public TokenStore() => tokens = JsonFile.Load<FileShape>(FilePath).Tokens;

    public List<Entry> All() { lock (gate) return tokens.Select(t => t.Copy()).ToList(); }
    public bool Any() { lock (gate) return tokens.Count > 0; }

    private void Save() => JsonFile.Save(FilePath, new FileShape { Tokens = tokens });

    /// <summary>A new token (for one user, or anyone); returns it as clients paste it.</summary>
    public string Create(string name, string userId = "")
    {
        string token = Token.Make(out var id, out var secret);
        lock (gate)
        {
            tokens.Add(new Entry
            {
                Id = id, Name = name.Trim().Length > 0 ? name.Trim() : $"Token {tokens.Count + 1}", Created = DateTime.UtcNow, UserId = userId,
                Secret = Convert.ToBase64String(ProtectedData.Protect(secret, null, DataProtectionScope.CurrentUser)),
            });
            Save();
        }
        ModuleLog.Server.Info($"Token {id} ({name}) made");
        Changed?.Invoke();
        return token;
    }

    /// <summary>The token as clients paste it (null if it's gone, or this Windows user can't unprotect it).</summary>
    public string? Text(string id) => SecretOf(id, touch: false) is { } secret ? Token.Format(id, secret) : null;

    public byte[]? SecretOf(string id, bool touch = true)
    {
        lock (gate)
        {
            var t = tokens.FirstOrDefault(x => x.Id == id);
            if (t == null) return null;
            try
            {
                var s = ProtectedData.Unprotect(Convert.FromBase64String(t.Secret), null, DataProtectionScope.CurrentUser);
                if (touch)
                {
                    t.LastUsed = DateTime.UtcNow;
                    Save();
                }
                return s;
            }
            catch (CryptographicException ex)
            {
                ModuleLog.Server.Warn($"Token {id} can't be read (made by another Windows user?)", ex);
                return null;
            }
        }
    }

    public void Rename(string id, string name)
    {
        lock (gate)
        {
            if (tokens.FirstOrDefault(x => x.Id == id) is not { } t || name.Trim().Length == 0) return;
            t.Name = name.Trim();
            Save();
        }
        Changed?.Invoke();
    }

    /// <summary>The user a token is for ("" for anyone, null if there's no such token).</summary>
    public string? UserOf(string id) { lock (gate) return tokens.FirstOrDefault(x => x.Id == id)?.UserId; }

    /// <summary>Ties a token made for anyone to one user.</summary>
    public void Assign(string id, string userId, string name)
    {
        lock (gate)
        {
            if (tokens.FirstOrDefault(x => x.Id == id) is not { } t) return;
            t.UserId = userId;
            t.Name = name;
            Save();
        }
        ModuleLog.Server.Info($"Token {id} tied to {name}");
        Changed?.Invoke();
    }

    /// <summary>Revokes every token of a user (they've been deleted).</summary>
    public void RevokeFor(string userId)
    {
        if (userId.Length == 0) return;
        foreach (var t in All().Where(t => t.UserId == userId)) Revoke(t.Id);
    }

    public void Revoke(string id)
    {
        lock (gate)
        {
            if (tokens.RemoveAll(x => x.Id == id) == 0) return;
            Save();
        }
        ModuleLog.Server.Info($"Token {id} revoked");
        Revoked?.Invoke(id);
        Changed?.Invoke();
    }
}
