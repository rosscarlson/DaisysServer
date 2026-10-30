using System.Collections.Concurrent;
using System.IO;

namespace DaisysServer.Modules;

public enum LogLevel { Info, Warning, Error }

public sealed record LogEntry(DateTime At, LogLevel Level, string Source, string Text);

/// <summary>
/// The server's log: one file a day in %LOCALAPPDATA%\DaisysServer\logs (kept 14 days), every line written straight
/// to disk, and the last thousand kept for the Log page. A module logs through its own (<see cref="For"/>).
/// </summary>
public sealed class ModuleLog
{
    private static readonly object gate = new();
    private static readonly ConcurrentQueue<LogEntry> recent = new();
    public static string Folder => Path.Combine(JsonFile.Home ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DaisysServer"), "logs");

    /// <summary>Every line written, from any thread.</summary>
    public static event Action<LogEntry>? Written;
    public static IReadOnlyList<LogEntry> Recent => recent.ToArray();

    public static ModuleLog Server { get; } = new("Server");
    public static ModuleLog For(string source) => new(source);

    private readonly string source;
    private ModuleLog(string source) => this.source = source;

    public void Info(string text) => Write(LogLevel.Info, text);
    public void Warn(string text, Exception? ex = null) => Write(LogLevel.Warning, ex == null ? text : $"{text}: {ex.Message}");
    public void Error(string text, Exception? ex = null) => Write(LogLevel.Error, ex == null ? text : $"{text}: {ex}");

    private void Write(LogLevel level, string text)
    {
        var e = new LogEntry(DateTime.Now, level, source, text);
        recent.Enqueue(e);
        while (recent.Count > 1000) recent.TryDequeue(out _);
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(Path.Combine(Folder, $"DaisysServer-{e.At:yyyy-MM-dd}.log"),
                    $"{e.At:HH:mm:ss.fff} {level,-7} [{source}] {text}{Environment.NewLine}");
            }
        }
        catch { }
        Written?.Invoke(e);
    }

    /// <summary>Logs older than 14 days deleted (at start).</summary>
    public static void Tidy()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Folder, "*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-14)) File.Delete(f);
        }
        catch { }
    }
}
