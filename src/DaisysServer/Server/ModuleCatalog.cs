using System.IO;
using System.Reflection;
using DaisysServer.Modules;

namespace DaisysServer.Server;

/// <summary>A module found in modules\: what it says it is, and (switched on) the module itself.</summary>
public sealed class LoadedModule
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Icon { get; init; } = "";
    public string Version { get; init; } = "";
    public bool Enabled { get; init; }
    public IServerModule? Instance { get; set; }
    public string? Problem { get; set; }
}

/// <summary>
/// The server's modules: every modules\&lt;Name&gt;\DaisysServer.&lt;Name&gt;.dll beside the program, each with a class
/// marked [ServerModule]. One switched off (Settings → Modules) is listed but never made, so none of its code runs.
/// </summary>
public sealed class ModuleCatalog
{
    public List<LoadedModule> Modules { get; } = new();

    public static string Folder => Path.Combine(AppContext.BaseDirectory, "modules");

    public void Load(ServerSettings settings, Func<LoadedModule, IModuleHost> hostFor)
    {
        if (!Directory.Exists(Folder)) return;
        foreach (var dir in Directory.EnumerateDirectories(Folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            string dll = Path.Combine(dir, $"DaisysServer.{Path.GetFileName(dir)}.dll");
            if (!File.Exists(dll)) continue;
            try
            {
                var asm = Assembly.LoadFrom(dll);
                foreach (var type in asm.GetTypes().Where(t => typeof(IServerModule).IsAssignableFrom(t) && !t.IsAbstract))
                {
                    if (type.GetCustomAttribute<ServerModuleAttribute>() is not { } a) continue;
                    if (Modules.Any(m => m.Id.Equals(a.Id, StringComparison.OrdinalIgnoreCase))) continue;
                    var m = new LoadedModule
                    {
                        Id = a.Id, Name = a.Name, Description = a.Description, Icon = a.Icon,
                        Version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "",
                        Enabled = !settings.DisabledModules.Contains(a.Id, StringComparer.OrdinalIgnoreCase),
                    };
                    Modules.Add(m);
                    if (!m.Enabled)
                    {
                        ModuleLog.Server.Info($"Module {m.Name}: switched off");
                        continue;
                    }
                    try
                    {
                        var instance = (IServerModule)Activator.CreateInstance(type)!;
                        instance.Start(hostFor(m));
                        m.Instance = instance;
                        ModuleLog.Server.Info($"Module {m.Name} {m.Version} started");
                    }
                    catch (Exception ex)
                    {
                        m.Problem = (ex is TargetInvocationException { InnerException: { } inner } ? inner : ex).Message;
                        ModuleLog.Server.Error($"Module {m.Name} couldn't start", ex);
                    }
                }
            }
            catch (Exception ex) { ModuleLog.Server.Error($"{Path.GetFileName(dll)} couldn't be loaded", ex); }
        }
    }

    public LoadedModule? Find(string id) => Modules.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        foreach (var m in Modules)
        {
            try { m.Instance?.Dispose(); }
            catch (Exception ex) { ModuleLog.Server.Warn($"Module {m.Name} didn't close cleanly", ex); }
            m.Instance = null;
        }
    }
}
