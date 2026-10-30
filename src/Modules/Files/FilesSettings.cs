using System.IO;
using DaisysServer.Modules;

namespace DaisysServer.Modules.Files;

/// <summary>Saved in %APPDATA%\DaisysServer\files.json: the drives shared (none until some are picked), and whether
/// basic users may browse them too (otherwise admins only).</summary>
public sealed class FilesSettings
{
    private static string FilePath => Path.Combine(JsonFile.SettingsFolder, "files.json");

    /// <summary>Their roots, as "C:\".</summary>
    public List<string> Drives { get; set; } = new();
    public bool BasicUsers { get; set; }

    public static FilesSettings Load() => JsonFile.Load<FilesSettings>(FilePath);

    public void Save()
    {
        lock (this) JsonFile.Save(FilePath, this);
    }

    public bool IsShared(string? root)
    {
        if (string.IsNullOrEmpty(root)) return false;
        lock (this) return Drives.Any(d => d.Equals(root, StringComparison.OrdinalIgnoreCase));
    }

    public string[] Shared()
    {
        lock (this) return Drives.ToArray();
    }

    public void Share(string root, bool on)
    {
        lock (this)
        {
            Drives.RemoveAll(d => d.Equals(root, StringComparison.OrdinalIgnoreCase));
            if (on) Drives.Add(root);
            Drives.Sort(StringComparer.OrdinalIgnoreCase);
        }
        Save();
    }
}
