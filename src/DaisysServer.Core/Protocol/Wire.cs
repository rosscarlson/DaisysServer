using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace DaisysServer.Protocol;

// This file is shared, word for word, by Daisy's Server (src/DaisysServer.Core/Protocol) and Daisy's App
// (src/DaisysApp.Core/Server): a change here goes there too. PROTOCOL.md describes it.

/// <summary>
/// A connection token: "dsk1_" then, base64url, 6 bytes of id (sent in the clear, so the server knows which token) and
/// 32 bytes of secret (never sent: both ends prove they have it by deriving the same keys).
/// </summary>
public static class Token
{
    public const string Prefix = "dsk1_";
    private const int IdBytes = 6, SecretBytes = 32;

    public static string Make(out string id, out byte[] secret)
    {
        var raw = RandomNumberGenerator.GetBytes(IdBytes + SecretBytes);
        id = Convert.ToHexString(raw, 0, IdBytes).ToLowerInvariant();
        secret = raw[IdBytes..];
        return Prefix + Base64Url(raw);
    }

    public static string Format(string id, byte[] secret) => Prefix + Base64Url([.. Convert.FromHexString(id), .. secret]);

    /// <summary>The token's id and secret; throws FormatException for anything that isn't one.</summary>
    public static (string Id, byte[] Secret) Parse(string token)
    {
        token = token.Trim();
        if (!token.StartsWith(Prefix, StringComparison.Ordinal)) throw new FormatException("That isn't a Daisy's Server token (it starts with dsk1_).");
        byte[] raw;
        try { raw = FromBase64Url(token[Prefix.Length..]); }
        catch (FormatException) { throw new FormatException("The token is damaged: copy it again."); }
        if (raw.Length != IdBytes + SecretBytes) throw new FormatException("The token is damaged: copy it again.");
        return (Convert.ToHexString(raw, 0, IdBytes).ToLowerInvariant(), raw[IdBytes..]);
    }

    public static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s + new string('=', (4 - s.Length % 4) % 4));
    }
}

/// <summary>The other end didn't prove it has the token (or it's revoked).</summary>
public sealed class WireAuthException(string message) : Exception(message);

/// <summary>
/// One encrypted connection. Frames are a 4-byte little-endian length then that many bytes. The server's first frame
/// and the client's answer are plain JSON (an ephemeral P-256 key and 32 random bytes each, and the token's id); both
/// derive two AES-256-GCM keys (one each way) with HKDF-SHA256 from the ECDH secret, salted with the token's secret.
/// After that every frame is one JSON message, encrypted: ciphertext then the 16-byte tag; the nonce isn't sent, it's
/// 4 zero bytes then a little-endian uint64 count of the frames sent that way. Thread-safe for one reader and any
/// number of senders.
/// </summary>
public sealed class Wire : IDisposable
{
    public const string App = "DaisysServer";
    public const int Version = 1;
    public const int MaxFrame = 32 << 20;

    private readonly Stream stream;
    private readonly AesGcm sendKey, readKey;
    private ulong sent, read;
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private int disposed;

    private Wire(Stream stream, byte[] sendKey, byte[] readKey)
    {
        this.stream = stream;
        this.sendKey = new AesGcm(sendKey, 16);
        this.readKey = new AesGcm(readKey, 16);
    }

    /// <summary>The client's side: the server's greeting, then ours with the token's id; the keys from both.</summary>
    public static async Task<Wire> ConnectAsync(Stream stream, string token, CancellationToken ct)
    {
        var (id, secret) = Token.Parse(token);
        var hello = await ReadPlainAsync(stream, ct) ?? throw new IOException("The server closed the connection.");
        if ((string?)hello["app"] != App) throw new IOException("That address isn't a Daisy's Server.");
        if ((int?)hello["v"] is not int v || v > Version) throw new IOException("The server is newer than this client: update it.");
        byte[] serverNonce = Convert.FromBase64String((string)hello["nonce"]!), serverPub = Convert.FromBase64String((string)hello["pub"]!);
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var nonce = RandomNumberGenerator.GetBytes(32);
        await WritePlainAsync(stream, new JsonObject
        {
            ["token"] = id, ["nonce"] = Convert.ToBase64String(nonce), ["pub"] = Convert.ToBase64String(ecdh.ExportSubjectPublicKeyInfo()),
        }, ct);
        var (c2s, s2c) = Keys(ecdh, serverPub, secret, serverNonce, nonce);
        return new Wire(stream, c2s, s2c);
    }

    /// <summary>The server's side: our greeting, the client's answer, the keys. <paramref name="secretOf"/> gives a
    /// token's secret from its id (null: no such token).</summary>
    public static async Task<(Wire Wire, string TokenId)> AcceptAsync(Stream stream, Func<string, byte[]?> secretOf, CancellationToken ct)
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var nonce = RandomNumberGenerator.GetBytes(32);
        await WritePlainAsync(stream, new JsonObject
        {
            ["app"] = App, ["v"] = Version, ["nonce"] = Convert.ToBase64String(nonce), ["pub"] = Convert.ToBase64String(ecdh.ExportSubjectPublicKeyInfo()),
        }, ct);
        var answer = await ReadPlainAsync(stream, ct) ?? throw new IOException("closed before the handshake");
        string id = (string?)answer["token"] ?? "";
        var secret = secretOf(id) ?? throw new WireAuthException($"unknown token {id}");
        byte[] clientNonce = Convert.FromBase64String((string)answer["nonce"]!), clientPub = Convert.FromBase64String((string)answer["pub"]!);
        var (c2s, s2c) = Keys(ecdh, clientPub, secret, nonce, clientNonce);
        return (new Wire(stream, s2c, c2s), id);
    }

    private static (byte[] C2S, byte[] S2C) Keys(ECDiffieHellman mine, byte[] theirPub, byte[] secret, byte[] serverNonce, byte[] clientNonce)
    {
        using var theirs = ECDiffieHellman.Create();
        theirs.ImportSubjectPublicKeyInfo(theirPub, out _);
        var shared = mine.DeriveRawSecretAgreement(theirs.PublicKey);
        byte[] info = [.. Encoding.ASCII.GetBytes("DaisysServer v1"), .. serverNonce, .. clientNonce];
        var keys = HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, 64, secret, info);
        return (keys[..32], keys[32..]);
    }

    private static byte[] Nonce(ulong count)
    {
        var n = new byte[12];
        BinaryPrimitives.WriteUInt64LittleEndian(n.AsSpan(4), count);
        return n;
    }

    public async Task SendAsync(JsonNode message, CancellationToken ct = default)
    {
        var plain = Encoding.UTF8.GetBytes(message.ToJsonString());
        await sendGate.WaitAsync(ct);
        try
        {
            var frame = new byte[plain.Length + 16];
            sendKey.Encrypt(Nonce(sent++), plain, frame.AsSpan(0, plain.Length), frame.AsSpan(plain.Length));
            await WriteFrameAsync(stream, frame, ct);
        }
        finally { sendGate.Release(); }
    }

    /// <summary>The next message; null when the other end has closed. Throws CryptographicException for a frame that
    /// isn't from someone with the keys (the wrong token, or tampering).</summary>
    public async Task<JsonObject?> ReadAsync(CancellationToken ct = default)
    {
        var frame = await ReadFrameAsync(stream, ct);
        if (frame == null) return null;
        if (frame.Length < 16) throw new CryptographicException("short frame");
        var plain = new byte[frame.Length - 16];
        readKey.Decrypt(Nonce(read++), frame.AsSpan(0, plain.Length), frame.AsSpan(plain.Length), plain);
        return JsonNode.Parse(plain) as JsonObject ?? throw new IOException("not a JSON object");
    }

    private static async Task WritePlainAsync(Stream s, JsonObject m, CancellationToken ct) => await WriteFrameAsync(s, Encoding.UTF8.GetBytes(m.ToJsonString()), ct);

    private static async Task<JsonObject?> ReadPlainAsync(Stream s, CancellationToken ct)
    {
        var frame = await ReadFrameAsync(s, ct, 4096);
        return frame == null ? null : JsonNode.Parse(frame) as JsonObject ?? throw new IOException("not a JSON object");
    }

    private static async Task WriteFrameAsync(Stream s, byte[] frame, CancellationToken ct)
    {
        var head = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(head, frame.Length);
        await s.WriteAsync(head, ct);
        await s.WriteAsync(frame, ct);
        await s.FlushAsync(ct);
    }

    private static async Task<byte[]?> ReadFrameAsync(Stream s, CancellationToken ct, int max = MaxFrame)
    {
        var head = new byte[4];
        if (!await FillAsync(s, head, ct)) return null;
        int length = BinaryPrimitives.ReadInt32LittleEndian(head);
        if (length < 0 || length > max) throw new IOException($"frame of {length} bytes");
        var frame = new byte[length];
        if (!await FillAsync(s, frame, ct)) return null;
        return frame;
    }

    private static async Task<bool> FillAsync(Stream s, byte[] buffer, CancellationToken ct)
    {
        int got = 0;
        while (got < buffer.Length)
        {
            int n = await s.ReadAsync(buffer.AsMemory(got), ct);
            if (n == 0) return false;
            got += n;
        }
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1) return;
        try { stream.Dispose(); } catch { }
        sendKey.Dispose();
        readKey.Dispose();
    }
}

/// <summary>
/// A user's keys, made on the client from their password, never sent: the master key (PBKDF2-HMAC-SHA256 of the
/// password, with the user's salt from the server), from it the sign-in proof sent to the server (<see cref="Auth"/>)
/// and the key that wraps the user's data key (<see cref="WrapKey"/>). The data key (32 random bytes, made by the first
/// client to sign in) is kept on the server wrapped, so changing the password only wraps it again. Everything a module
/// stores is sealed with the data key on the client: the server keeps it but can't read it.
/// </summary>
public static class Vault
{
    public const int Iterations = 210_000;

    public static byte[] Master(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);

    public static byte[] Auth(byte[] master) => HMACSHA256.HashData(master, "DaisysServer auth"u8.ToArray());
    public static byte[] WrapKey(byte[] master) => HMACSHA256.HashData(master, "DaisysServer wrap"u8.ToArray());

    /// <summary>What the server keeps to check a sign-in: SHA-256 of its salt then the proof.</summary>
    public static byte[] Verifier(byte[] auth, byte[] verifierSalt) => SHA256.HashData([.. verifierSalt, .. auth]);

    /// <summary>AES-256-GCM, base64 of the 12-byte random nonce, the ciphertext and the 16-byte tag.</summary>
    public static string Seal(byte[] key, byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var r = new byte[12 + plain.Length + 16];
        nonce.CopyTo(r, 0);
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, r.AsSpan(12, plain.Length), r.AsSpan(12 + plain.Length));
        return Convert.ToBase64String(r);
    }

    /// <summary>What <see cref="Seal"/> sealed; throws CryptographicException for the wrong key or a damaged one.</summary>
    public static byte[] Open(byte[] key, string sealedText)
    {
        var b = Convert.FromBase64String(sealedText);
        if (b.Length < 28) throw new CryptographicException("too short");
        var plain = new byte[b.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(b.AsSpan(0, 12), b.AsSpan(12, plain.Length), b.AsSpan(12 + plain.Length), plain);
        return plain;
    }

    public static string SealText(byte[] key, string text) => Seal(key, Encoding.UTF8.GetBytes(text));
    public static string OpenText(byte[] key, string sealedText) => Encoding.UTF8.GetString(Open(key, sealedText));
}
