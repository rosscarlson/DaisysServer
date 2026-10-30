using System.IO;
using DaisysServer.Modules;

namespace DaisysServer.Server;

/// <summary>Saved in %APPDATA%\DaisysServer\settings.json.</summary>
public sealed class ServerSettings
{
    public const int DefaultPort = 38900;
    private static string FilePath => Path.Combine(JsonFile.SettingsFolder, "settings.json");

    /// <summary>The name clients see.</summary>
    public string Name { get; set; } = Environment.MachineName;
    public int Port { get; set; } = DefaultPort;
    /// <summary>Listen as soon as the server starts.</summary>
    public bool ListenOnStart { get; set; } = true;
    public bool StartWithWindows { get; set; }
    /// <summary>Closing the window keeps the server running in the tray.</summary>
    public bool KeepInTray { get; set; } = true;
    public bool CheckUpdatesOnLaunch { get; set; } = true;
    /// <summary>Modules switched off (by id): not loaded.</summary>
    public List<string> DisabledModules { get; set; } = new();
    /// <summary>A random secret of this server's (base64): the made-up salts for user names it doesn't have.</summary>
    public string Secret { get; set; } = "";
    public string? LastPage { get; set; }
    public double[]? Bounds { get; set; }

    public static ServerSettings Load()
    {
        var s = JsonFile.Load<ServerSettings>(FilePath);
        if (s.Secret.Length == 0)
        {
            s.Secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            s.Save();
        }
        if (s.Port is < 1 or > 65535) s.Port = DefaultPort;
        return s;
    }

    public void Save() => JsonFile.Save(FilePath, this);
}
