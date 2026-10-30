using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Windows;
using DaisysServer.Modules;
using DaisysServer.Protocol;
using Microsoft.Win32;

namespace DaisysServer.Modules.Files;

/// <summary>
/// This PC's files, for a client to browse: the drives picked in Settings → Shared drives, read-only. A client asks
/// (roots, list, stat, read, thumb, each with a rid) and gets one answer per question; reads come in pieces it asks for
/// (up to 1 MB each), so a big file never queues up here. Unlike other modules, what this one sends isn't sealed with
/// the user's key: it's the server's own files, so they're only encrypted on the connection. Admins only unless
/// basic users are let in too. Paths are checked against the shared drives, links included.
/// </summary>
[ServerModule("files", "Files", Description = "This PC's shared drives, for clients to browse, preview and open (read-only).", Icon = "")]
public sealed class FilesModule : IServerModule
{
    public const int MaxRead = 1 << 20, DefaultRead = 256 << 10, DefaultList = 5000, MaxList = 20000;

    private IModuleHost host = null!;
    private FilesSettings settings = null!;
    private FilesSettingsView? settingsView;

    public void Start(IModuleHost host)
    {
        this.host = host;
        settings = FilesSettings.Load();
    }

    public FrameworkElement? View => null;
    public FrameworkElement? SettingsView => settingsView ??= new FilesSettingsView(settings);

    public async Task ServeAsync(IModuleChannel channel, CancellationToken ct)
    {
        string who = $"{channel.User.Name} on {channel.Client}";
        host.Log.Info($"{who} opened Files");
        // four questions at a time: the rest wait in the channel
        var gate = new SemaphoreSlim(4);
        while (await channel.ReadAsync(ct) is { } m)
        {
            await gate.WaitAsync(ct);
            _ = Task.Run(async () =>
            {
                try { channel.Post(await AnswerAsync(channel, who, m)); }
                catch (Exception ex) { host.Log.Warn($"{who}: an answer couldn't be sent", ex); }
                finally { gate.Release(); }
            }, CancellationToken.None);
        }
        host.Log.Info($"{who} closed Files");
    }

    private async Task<JsonObject> AnswerAsync(IModuleChannel channel, string who, JsonObject m)
    {
        string op = ServerClient.Str(m, "op");
        JsonObject answer;
        try
        {
            if (!channel.User.Admin && !settings.BasicUsers) throw new FilesException("Only admins can browse files on this server.");
            answer = op switch
            {
                "roots" => Roots(),
                "list" => List(m),
                "stat" => Stat(m),
                "read" => Read(m, who),
                "thumb" => await ThumbAsync(m),
                _ => throw new FilesException($"This server can't do \"{op}\" yet: update it."),
            };
            answer["op"] = op;
        }
        catch (Exception ex)
        {
            string message = ex switch
            {
                FilesException => ex.Message,
                FileNotFoundException or DirectoryNotFoundException => "It isn't there any more.",
                UnauthorizedAccessException => "Windows didn't allow that (access denied).",
                IOException => ex.Message,
                _ => $"That couldn't be done: {ex.Message}",
            };
            answer = new JsonObject { ["op"] = "error", ["message"] = message };
        }
        if (m["rid"] is JsonNode rid) answer["rid"] = rid.DeepClone();
        return answer;
    }

    // ---- the questions

    /// <summary>{op: "roots"}: the shared drives, in order, with their space.</summary>
    private JsonObject Roots()
    {
        var list = new JsonArray();
        foreach (var root in settings.Shared()) list.Add(Drive(root));
        return new JsonObject { ["roots"] = list };
    }

    internal static JsonObject Drive(string root)
    {
        var o = new JsonObject { ["path"] = root };
        string letter = root.TrimEnd('\\');
        try
        {
            var d = new DriveInfo(root);
            o["kind"] = d.DriveType switch
            {
                DriveType.Fixed => "fixed", DriveType.Removable => "removable", DriveType.Network => "network",
                DriveType.CDRom => "cd", DriveType.Ram => "ram", _ => "other",
            };
            bool ready = d.IsReady;
            o["ready"] = ready;
            string label = ready ? d.VolumeLabel : "";
            if (label.Length == 0)
                label = d.DriveType switch
                {
                    DriveType.Removable => "USB Drive", DriveType.Network => "Network Drive", DriveType.CDRom => "CD Drive",
                    DriveType.NoRootDirectory => "Not connected", _ => "Local Disk",
                };
            o["label"] = ready ? d.VolumeLabel : "";
            o["name"] = $"{label} ({letter})";
            if (ready)
            {
                o["format"] = d.DriveFormat;
                o["total"] = d.TotalSize;
                o["free"] = d.AvailableFreeSpace;
            }
        }
        catch
        {
            o["kind"] = "other";
            o["ready"] = false;
            o["label"] = "";
            o["name"] = $"Not connected ({letter})";
        }
        return o;
    }

    /// <summary>{op: "list", path, skip?, max?}: a folder's folders then files, in Explorer's order.</summary>
    private JsonObject List(JsonObject m)
    {
        string full = Resolve(m);
        var dir = new DirectoryInfo(full);
        if (!dir.Exists) throw new FilesException(File.Exists(full) ? "That's a file, not a folder." : "It isn't there any more.");
        var all = dir.EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, ReturnSpecialDirectories = false })
            .OrderBy(i => i is DirectoryInfo ? 0 : 1).ThenBy(i => i.Name, Logical.Instance).ToList();
        int skip = Math.Max(0, (int?)m["skip"] ?? 0);
        int max = Math.Clamp((int?)m["max"] ?? DefaultList, 1, MaxList);
        var items = new JsonArray();
        foreach (var i in all.Skip(skip).Take(max)) items.Add(Entry(i));
        return new JsonObject
        {
            ["path"] = dir.FullName, ["parent"] = dir.Parent?.FullName, ["count"] = all.Count, ["skip"] = skip, ["items"] = items,
        };
    }

    /// <summary>{op: "stat", path}: one file or folder's details.</summary>
    private JsonObject Stat(JsonObject m)
    {
        string full = Resolve(m);
        FileSystemInfo i = Directory.Exists(full) ? new DirectoryInfo(full) : new FileInfo(full);
        if (!i.Exists) throw new FilesException("It isn't there any more.");
        var o = Entry(i);
        o["path"] = i.FullName;
        o["parent"] = (i as DirectoryInfo)?.Parent?.FullName ?? (i as FileInfo)?.DirectoryName;
        o["accessed"] = Ms(i.LastAccessTimeUtc);
        o["attributes"] = i.Attributes.ToString();
        if (i is FileInfo)
        {
            o["ext"] = i.Extension.ToLowerInvariant();
            if (Mime(i.Extension) is { } mime) o["mime"] = mime;
        }
        if (i.LinkTarget is { } target) o["target"] = target;
        return o;
    }

    /// <summary>{op: "read", path, offset, length}: a piece of a file (up to 1 MB), base64.</summary>
    private JsonObject Read(JsonObject m, string who)
    {
        string full = Resolve(m);
        if (Directory.Exists(full)) throw new FilesException("That's a folder, not a file.");
        long offset = Math.Max(0, (long?)m["offset"] ?? 0);
        int length = Math.Clamp((int?)m["length"] ?? DefaultRead, 1, MaxRead);
        using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
        long size = fs.Length;
        var buffer = new byte[(int)Math.Min(length, Math.Max(0, size - offset))];
        int n = 0;
        if (buffer.Length > 0)
        {
            fs.Position = offset;
            while (n < buffer.Length && fs.Read(buffer, n, buffer.Length - n) is int got and > 0) n += got;
        }
        if (offset == 0) host.Log.Info($"{who} opened {full}");
        return new JsonObject
        {
            ["path"] = full, ["offset"] = offset, ["length"] = n, ["size"] = size, ["eof"] = offset + n >= size,
            ["modified"] = Ms(File.GetLastWriteTimeUtc(full)), ["data"] = Convert.ToBase64String(buffer, 0, n),
        };
    }

    /// <summary>{op: "thumb", path, size?}: Windows' thumbnail (as Explorer shows), or data null when it has none.</summary>
    private async Task<JsonObject> ThumbAsync(JsonObject m)
    {
        string full = Resolve(m);
        if (!File.Exists(full) && !Directory.Exists(full)) throw new FilesException("It isn't there any more.");
        int size = Math.Clamp((int?)m["size"] ?? 256, 16, 1024);
        var t = await Thumbnails.GetAsync(full, size);
        var o = new JsonObject { ["path"] = full };
        if (t is { } got)
        {
            o["type"] = got.Type;
            o["w"] = got.Width;
            o["h"] = got.Height;
            o["data"] = Convert.ToBase64String(got.Data);
        }
        else o["data"] = null;
        return o;
    }

    // ---- paths

    /// <summary>The question's "path", made full and checked: on a shared drive, and no link along it leading off
    /// them. No UNC or device paths, and no streams (a ':' past the drive's).</summary>
    private string Resolve(JsonObject m) => Resolve(ServerClient.Str(m, "path"), settings);

    internal static string Resolve(string path, FilesSettings settings)
    {
        path = path.Replace('/', '\\');
        if (path.Length == 2 && path[1] == ':') path += "\\";
        if (path.Length < 3 || path.StartsWith(@"\\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(path) || path.IndexOf(':', 2) >= 0 || path.IndexOfAny(['*', '?', '"', '<', '>', '|']) >= 0)
            throw new FilesException("That isn't a path on a shared drive.");
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full)!;
        if (!settings.IsShared(root)) throw new FilesException("That isn't on a shared drive.");
        string at = root;
        foreach (var part in full[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            at = Path.Combine(at, part);
            FileSystemInfo i = Directory.Exists(at) ? new DirectoryInfo(at) : new FileInfo(at);
            if (!i.Exists || i.LinkTarget == null) continue;
            var target = i.ResolveLinkTarget(returnFinalTarget: true);
            if (target == null || !settings.IsShared(Path.GetPathRoot(target.FullName)))
                throw new FilesException("That leads off the shared drives.");
        }
        return full;
    }

    internal static JsonObject Entry(FileSystemInfo i)
    {
        var o = new JsonObject { ["name"] = i.Name, ["dir"] = i is DirectoryInfo };
        if (i is FileInfo f) o["size"] = f.Length;
        o["modified"] = Ms(i.LastWriteTimeUtc);
        o["created"] = Ms(i.CreationTimeUtc);
        var a = i.Attributes;
        if (a.HasFlag(FileAttributes.Hidden)) o["hidden"] = true;
        if (a.HasFlag(FileAttributes.System)) o["system"] = true;
        if (a.HasFlag(FileAttributes.ReadOnly)) o["readonly"] = true;
        if (i.LinkTarget != null) o["link"] = true;
        // (OneDrive and the like: not on this PC until it's read)
        if (a.HasFlag(FileAttributes.Offline) || ((int)a & 0x400000) != 0) o["online"] = true;
        return o;
    }

    private static long Ms(DateTime utc) => utc.Year < 1601 ? 0 : new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    private static readonly ConcurrentDictionary<string, string?> mimes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The type Windows has for an extension (HKCR\.ext, Content Type), or null.</summary>
    internal static string? Mime(string ext) => ext.Length < 2 ? null : mimes.GetOrAdd(ext, e =>
    {
        try { return Registry.ClassesRoot.OpenSubKey(e)?.GetValue("Content Type") as string; }
        catch { return null; }
    });

    public void UserRemoved(ServerUser user) { }

    public void Dispose() { }

    /// <summary>Names in Explorer's order ("2" before "10").</summary>
    private sealed class Logical : IComparer<string>
    {
        public static readonly Logical Instance = new();
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string a, string b);
        public int Compare(string? a, string? b) => StrCmpLogicalW(a ?? "", b ?? "");
    }
}

/// <summary>A question that can't be answered, with the reason for the client.</summary>
public sealed class FilesException(string message) : Exception(message);
