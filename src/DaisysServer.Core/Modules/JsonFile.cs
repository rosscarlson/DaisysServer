using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DaisysServer.Modules;

/// <summary>A settings or data file: read (the defaults if it's missing or damaged), and written safely (a temporary
/// file moved over it, so a crash never leaves half of one).</summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Set (a folder), everything the server keeps goes in it instead: for trying a build without touching the
    /// real server's settings, users and data.</summary>
    public static string? Home { get; } = Environment.GetEnvironmentVariable("DAISYSSERVER_HOME") is { Length: > 0 } h ? h : null;

    /// <summary>%APPDATA%\DaisysServer: settings, users and tokens.</summary>
    public static string SettingsFolder => Home ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DaisysServer");

    /// <summary>%LOCALAPPDATA%\DaisysServer\data: what modules keep for each user.</summary>
    public static string DataFolder => Path.Combine(Home ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DaisysServer"), "data");

    public static T Load<T>(string path) where T : new()
    {
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (Exception ex) { ModuleLog.Server.Warn($"{Path.GetFileName(path)} couldn't be read", ex); }
        return new T();
    }

    public static void Save<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) { ModuleLog.Server.Warn($"{Path.GetFileName(path)} couldn't be saved", ex); }
    }
}
