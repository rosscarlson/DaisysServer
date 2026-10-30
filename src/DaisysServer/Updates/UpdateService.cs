using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using DaisysServer.Modules;

namespace DaisysServer.Updates;

public sealed record UpdateInfo(Version Version, string Tag, string ReleaseUrl, string AssetUrl, string AssetName, long Size, string? Sha256);

/// <summary>
/// As Daisy's App's: checks GitHub Releases (rosscarlson/DaisysServer) for a newer version, downloads its installer
/// (DaisysServer-Setup-x.y.z.exe), checks it against the SHA-256 GitHub publishes for it, and runs it silently
/// (Windows asks for admin approval); the installer closes the server, upgrades it and starts it again.
/// </summary>
public static class UpdateService
{
    public const string Owner = "rosscarlson";
    public const string Repo = "DaisysServer";
    public const string AssetPrefix = "DaisysServer-Setup-";
    public static string RepoUrl => $"https://github.com/{Owner}/{Repo}";
    public static string ReleasesUrl => RepoUrl + "/releases";

    private static readonly Lazy<HttpClient> Http = new(() =>
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"DaisysServer/{Display(AppInfo.Version)}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    });

    public static string Display(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}";

    /// <summary>"v1.2.3", "1.2.3-beta" or "1.2.3+sha" → 1.2.3; null if it isn't one.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().TrimStart('v', 'V');
        int cut = text.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) text = text[..cut];
        if (!Version.TryParse(text, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    /// <summary>The latest release, if it's newer than this one; null if not (or there are none yet).</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var response = await Http.Value.GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = ParseVersion(tag);
        if (version == null || version <= AppInfo.Version) return null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (!name.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            string? sha = asset.TryGetProperty("digest", out var d) && d.GetString() is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest["sha256:".Length..] : null;
            return new UpdateInfo(version, tag, root.GetProperty("html_url").GetString() ?? ReleasesUrl,
                asset.GetProperty("browser_download_url").GetString()!, name, asset.GetProperty("size").GetInt64(), sha);
        }
        return null;
    }

    /// <summary>Downloads the installer to %TEMP%\DaisysServer-Update, resuming a stalled download (up to four tries), and
    /// checks its SHA-256. <paramref name="progress"/> (0–1) is called on the downloading thread.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, Action<double>? progress, CancellationToken ct = default)
    {
        string dir = Path.Combine(Path.GetTempPath(), "DaisysServer-Update");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, Path.GetFileName(update.AssetName));
        if (File.Exists(path)) File.Delete(path);
        long done = 0, total = update.Size;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, update.AssetUrl);
                if (done > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(done, null);
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(TimeSpan.FromSeconds(30));
                using var response = await Http.Value.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
                response.EnsureSuccessStatusCode();
                bool resumed = done > 0 && response.StatusCode == HttpStatusCode.PartialContent;
                if (!resumed) done = 0;
                if (!resumed && response.Content.Headers.ContentLength is { } length) total = length;
                await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
                await using var file = new FileStream(path, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                while (true)
                {
                    stall.CancelAfter(TimeSpan.FromSeconds(30));
                    int read = await source.ReadAsync(buffer, stall.Token);
                    if (read == 0) break;
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Invoke(Math.Min(1, (double)done / total));
                }
                if (total > 0 && done < total) throw new IOException($"The download ended early ({done} of {total} bytes).");
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < 4 && ex is HttpRequestException or IOException or OperationCanceledException)
            {
                ModuleLog.Server.Warn($"Update download: try {attempt} stopped at {done} of {total} bytes ({ex.Message}); trying again");
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
            }
        }
        if (update.Sha256 != null)
        {
            await using var check = File.OpenRead(path);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(check, ct));
            if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidOperationException("The downloaded installer failed its integrity check.");
            }
        }
        return path;
    }

    /// <summary>Starts the installer silently (Windows asks for admin approval); the caller then exits.</summary>
    public static void LaunchInstaller(string path) =>
        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });

    public static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
