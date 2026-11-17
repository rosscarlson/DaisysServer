using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace DaisysServer.Protocol;

// This file is shared, word for word, by Daisy's Server (src/DaisysServer.Core/Protocol) and Daisy's App
// (src/DaisysApp.Core/Server): a change here goes there too. PROTOCOL.md describes it.

/// <summary>
/// The server's key: a P-256 key pair it makes once and keeps. Clients know it by its fingerprint (base64url, no padding,
/// of SHA-256 of the public key's SubjectPublicKeyInfo), built in or kept from the first connection, and connect only
/// to the server that has it: nobody else can make the connection's keys (<see cref="Wire"/>). Not a secret.
/// </summary>
public static class ServerKey
{
    public static string Fingerprint(byte[] spki) => Base64Url(SHA256.HashData(spki));

    public static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>The server's key isn't the one the client expected.</summary>
public sealed class WireAuthException(string message) : Exception(message);

/// <summary>
/// One encrypted connection. Frames are a 4-byte little-endian length then that many bytes. The server's first frame
/// and the client's answer are plain JSON: an ephemeral P-256 key and 32 random bytes each, and the server's own key
/// (<see cref="ServerKey"/>). Both derive two AES-256-GCM keys (one each way) with HKDF-SHA256 from two ECDH secrets
/// (the two ephemeral keys; the client's ephemeral and the server's key), so only the server with that key can. After
/// that every frame is one JSON message, encrypted: ciphertext then the 16-byte tag; the nonce isn't sent, it's 4 zero
/// bytes then a little-endian uint64 count of the frames sent that way. Thread-safe for one reader and any number of
/// senders.
/// </summary>
public sealed class Wire : IDisposable
{
    public const string App = "DaisysServer";
    /// <summary>2 since Daisy's Server 0.3.0: the server's key, no tokens.</summary>
    public const int Version = 2;
    public const int MaxFrame = 32 << 20;

    private readonly Stream stream;
    private readonly AesGcm sendKey, readKey;
    private ulong sent, read;
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private int disposed;

    /// <summary>The server's key's fingerprint (the client's side).</summary>
    public string ServerKey { get; private init; } = "";

    private Wire(Stream stream, byte[] sendKey, byte[] readKey)
    {
        this.stream = stream;
        this.sendKey = new AesGcm(sendKey, 16);
        this.readKey = new AesGcm(readKey, 16);
    }

    /// <summary>The client's side: the server's greeting (its key, which must be <paramref name="serverKey"/>'s
    /// fingerprint unless that's empty: then any, kept by the caller), then ours; the keys from both.</summary>
    public static async Task<Wire> ConnectAsync(Stream stream, string? serverKey, CancellationToken ct)
    {
        var hello = await ReadPlainAsync(stream, ct) ?? throw new IOException("The server closed the connection.");
        if ((string?)hello["app"] != App) throw new IOException("That address isn't a Daisy's Server.");
        if ((int?)hello["v"] is not int v || v > Version) throw new IOException("The server is newer than this client: update it.");
        if (v < Version) throw new IOException("The server is older than this client: update it (to 0.3.0 or later).");
        byte[] serverNonce = Convert.FromBase64String((string)hello["nonce"]!), serverPub = Convert.FromBase64String((string)hello["pub"]!),
            key = Convert.FromBase64String((string)hello["key"]!);
        string fingerprint = global::DaisysServer.Protocol.ServerKey.Fingerprint(key);
        if (!string.IsNullOrEmpty(serverKey) && serverKey != fingerprint)
            throw new WireAuthException("The server's key isn't the one this client knows: it may not be your server.");
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var nonce = RandomNumberGenerator.GetBytes(32);
        byte[] pub = ecdh.ExportSubjectPublicKeyInfo();
        await WritePlainAsync(stream, new JsonObject { ["nonce"] = Convert.ToBase64String(nonce), ["pub"] = Convert.ToBase64String(pub) }, ct);
        using var server = Import(serverPub);
        using var serverKeyPub = Import(key);
        byte[] ikm = [.. ecdh.DeriveRawSecretAgreement(server.PublicKey), .. ecdh.DeriveRawSecretAgreement(serverKeyPub.PublicKey)];
        var (c2s, s2c) = Keys(ikm, serverPub, pub, key, serverNonce, nonce);
        return new Wire(stream, c2s, s2c) { ServerKey = fingerprint };
    }

    /// <summary>The server's side: our greeting (with <paramref name="key"/>'s public half), the client's answer, the
    /// keys.</summary>
    public static async Task<Wire> AcceptAsync(Stream stream, ECDiffieHellman key, CancellationToken ct)
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var nonce = RandomNumberGenerator.GetBytes(32);
        byte[] pub = ecdh.ExportSubjectPublicKeyInfo(), keyPub = key.ExportSubjectPublicKeyInfo();
        await WritePlainAsync(stream, new JsonObject
        {
            ["app"] = App, ["v"] = Version, ["nonce"] = Convert.ToBase64String(nonce), ["pub"] = Convert.ToBase64String(pub),
            ["key"] = Convert.ToBase64String(keyPub),
        }, ct);
        var answer = await ReadPlainAsync(stream, ct) ?? throw new IOException("closed before the handshake");
        byte[] clientNonce = Convert.FromBase64String((string)answer["nonce"]!), clientPub = Convert.FromBase64String((string)answer["pub"]!);
        using var client = Import(clientPub);
        byte[] ikm = [.. ecdh.DeriveRawSecretAgreement(client.PublicKey), .. key.DeriveRawSecretAgreement(client.PublicKey)];
        var (c2s, s2c) = Keys(ikm, pub, clientPub, keyPub, nonce, clientNonce);
        return new Wire(stream, s2c, c2s);
    }

    private static ECDiffieHellman Import(byte[] spki)
    {
        var k = ECDiffieHellman.Create();
        k.ImportSubjectPublicKeyInfo(spki, out _);
        return k;
    }

    /// <summary>HKDF-SHA256: the two ECDH secrets, salted with SHA-256 of the three public keys (server's ephemeral,
    /// client's, server's key), the info "DaisysServer v2" then both nonces.</summary>
    private static (byte[] C2S, byte[] S2C) Keys(byte[] ikm, byte[] serverPub, byte[] clientPub, byte[] key, byte[] serverNonce, byte[] clientNonce)
    {
        byte[] salt = SHA256.HashData([.. serverPub, .. clientPub, .. key]);
        byte[] info = [.. Encoding.ASCII.GetBytes("DaisysServer v2"), .. serverNonce, .. clientNonce];
        var keys = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 64, salt, info);
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
    /// isn't from someone with the keys (tampering).</summary>
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
