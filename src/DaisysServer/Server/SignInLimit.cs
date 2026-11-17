using DaisysServer.Modules;

namespace DaisysServer.Server;

/// <summary>
/// Wrong passwords counted by IP address (since 0.3.0, when tokens went and anyone can reach sign-in): 5 in 15 minutes
/// and that address can't sign in for 15 minutes after the last. A right one clears it. Kept in memory only.
/// </summary>
public sealed class SignInLimit
{
    public const int Tries = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly object gate = new();
    private readonly Dictionary<string, List<DateTime>> wrong = new();

    /// <summary>How long that address must wait, or null.</summary>
    public TimeSpan? Blocked(string address)
    {
        lock (gate)
        {
            if (!wrong.TryGetValue(address, out var times)) return null;
            times.RemoveAll(t => DateTime.UtcNow - t > Window);
            if (times.Count == 0) { wrong.Remove(address); return null; }
            return times.Count >= Tries ? Window - (DateTime.UtcNow - times[^1]) : null;
        }
    }

    public void Failed(string address)
    {
        bool now;
        lock (gate)
        {
            if (!wrong.TryGetValue(address, out var times)) wrong[address] = times = new();
            times.Add(DateTime.UtcNow);
            now = times.Count == Tries;
            // (old addresses forgotten now and then)
            if (wrong.Count > 1000)
                foreach (var a in wrong.Where(kv => DateTime.UtcNow - kv.Value[^1] > Window).Select(kv => kv.Key).ToList()) wrong.Remove(a);
        }
        if (now) ModuleLog.Server.Warn($"{address}: {Tries} wrong passwords, can't sign in for {Window.TotalMinutes:0} minutes");
    }

    public void Succeeded(string address) { lock (gate) wrong.Remove(address); }
}
