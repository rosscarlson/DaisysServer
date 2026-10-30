using System.Text.Json.Nodes;
using System.Windows;

namespace DaisysServer.Modules;

/// <summary>
/// Describes a server module: its id (what a client names in {op: "open", module}), its name and what it is (shown in
/// Modules), and its icon (a Segoe Fluent Icons glyph, for its page's tab).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ServerModuleAttribute(string id, string name) : Attribute
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Description { get; init; } = "";
    public string Icon { get; init; } = "";
}

/// <summary>A signed-in user, as a module sees them: their data is theirs alone (<see cref="IModuleHost.FolderFor"/>).</summary>
public sealed record ServerUser(string Id, string Name, bool Admin);

/// <summary>
/// A server module: a class library in modules\&lt;Name&gt;\DaisysServer.&lt;Name&gt;.dll with one class implementing this,
/// marked [ServerModule]. The server makes one, calls <see cref="Start"/>, and hands it every channel a client opens to
/// it; a module switched off in Modules isn't loaded at all, and clients asking for it are told so.
/// </summary>
public interface IServerModule : IDisposable
{
    /// <summary>Called once, on the UI thread, after it's made.</summary>
    void Start(IModuleHost host);

    /// <summary>One client's channel, until it ends (on a background thread; the module owns the channel).</summary>
    Task ServeAsync(IModuleChannel channel, CancellationToken ct);

    /// <summary>Its page in the server's window (a tab of its own), or null for none.</summary>
    FrameworkElement? View { get; }

    /// <summary>A user was deleted, or their password reset (their data can't be opened any more): forget it.</summary>
    void UserRemoved(ServerUser user);
}

/// <summary>A conversation between a client's module and this one, over that client's encrypted connection.</summary>
public interface IModuleChannel : IDisposable
{
    /// <summary>Who signed in on that connection.</summary>
    ServerUser User { get; }
    /// <summary>The client's name (its PC or device), and the id of its module on the client side (its "from").</summary>
    string Client { get; }
    string From { get; }
    /// <summary>Sends a message (queued: they go in order).</summary>
    void Post(JsonNode message);
    /// <summary>The next message, or null when the client has closed the channel or gone.</summary>
    Task<JsonObject?> ReadAsync(CancellationToken ct = default);
}

/// <summary>What the server gives a module.</summary>
public interface IModuleHost
{
    ModuleLog Log { get; }
    /// <summary>The module's folder for one user's data (made if it isn't there): everything a client sends is sealed
    /// with that user's key already, so it's kept as it comes.</summary>
    string FolderFor(ServerUser user);
    /// <summary>The users there are now.</summary>
    IReadOnlyList<ServerUser> Users { get; }
    /// <summary>Raised when the users (or who's connected) change, on any thread.</summary>
    event Action? Changed;
}
