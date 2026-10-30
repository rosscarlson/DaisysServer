using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysServer.Modules;
using DaisysServer.Server;

namespace DaisysServer.Pages;

/// <summary>
/// Status: whether it's listening (Start / Stop), the addresses clients use, the steps to get going while there are
/// no users or tokens, and the connections: who, from where, signed in as whom, using which modules.
/// </summary>
internal sealed class StatusPage : UserControl
{
    private readonly ServerHost server;
    private readonly Action<string> go;
    private readonly TextBlock state = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock dot = new() { Text = "●", FontSize = 18, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button toggle;
    private readonly TextBlock addresses = new() { TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
    private readonly TextBlock publicAddress = Ui.Secondary();
    private readonly Border gettingStarted;
    private readonly StackPanel steps = new();
    private readonly Grid table = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };

    public StatusPage(ServerHost server, Action<string> go)
    {
        this.server = server;
        this.go = go;
        toggle = Ui.Button("", "Stop", null, () => { if (server.Listening) server.Stop(); else server.Start(); });
        toggle.Margin = new Thickness(20, 0, 0, 0);
        var top = new StackPanel();
        top.Children.Add(Ui.Header("Server"));
        top.Children.Add(Ui.Row(dot, state, toggle));
        top.Children.Add(addresses);
        var find = Ui.Button("", "Show public address", "Asks api.ipify.org what this network's address is on the internet", () => _ = FindPublicAsync());
        find.Margin = new Thickness(0, 8, 12, 0);
        publicAddress.VerticalAlignment = VerticalAlignment.Center;
        publicAddress.Margin = new Thickness(0, 8, 0, 0);
        top.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { find, publicAddress } });

        var start = new StackPanel();
        start.Children.Add(Ui.Header("Getting started"));
        start.Children.Add(steps);
        gettingStarted = Ui.Card(start);

        var conn = new StackPanel();
        conn.Children.Add(Ui.Header("Connected"));
        conn.Children.Add(table);

        Content = Ui.Page(Ui.Card(top), gettingStarted, Ui.Card(conn));
        server.Changed += () => Dispatcher.BeginInvoke(Refresh);
        timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { Refresh(); timer.Start(); }
            else timer.Stop();
        };
        Refresh();
    }

    private void Refresh()
    {
        if (server.Listening)
        {
            state.Text = $"Listening on port {server.ListeningPort}";
            dot.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
        }
        else
        {
            state.Text = server.Problem != null ? $"Not listening: {server.Problem}" : "Not listening";
            dot.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        }
        ((TextBlock)((StackPanel)toggle.Content).Children[1]).Text = server.Listening ? "Stop" : "Start";
        ((TextBlock)((StackPanel)toggle.Content).Children[0]).Text = server.Listening ? "" : "";

        var ips = LocalAddresses();
        addresses.Text = $"Clients connect to this PC's address and port {server.Settings.Port}, with a token and a user's name and password.\n" +
            (ips.Count > 0 ? $"On this network: {string.Join(", ", ips)}\n" : "") +
            $"From the internet: forward TCP port {server.Settings.Port} on your router to this PC, and use your public address (or a DNS name for it).";

        steps.Children.Clear();
        bool users = server.Users.Any(), tokens = server.Tokens.Any();
        gettingStarted.Visibility = users && tokens ? Visibility.Collapsed : Visibility.Visible;
        void Step(int n, string text, bool done, string page, string button)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var t = new TextBlock { Text = $"{(done ? "✓" : n + ".")}  {text}", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            if (done) t.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            if (!done)
            {
                var b = Ui.Button("", button, null, () => go(page));
                b.Margin = new Thickness(12, 0, 0, 0);
                DockPanel.SetDock(b, Dock.Right);
                row.Children.Add(b);
            }
            row.Children.Add(t);
            steps.Children.Add(row);
        }
        Step(1, "Add a user for each person (admin for yourself). Each user's data is theirs alone, sealed with their password.", users, "users", "Users");
        Step(2, "Make a token and paste it into each client (Daisy's App: Settings → Tools → Notepad → Daisy's Server) with this server's address and port.", tokens, "tokens", "Tokens");
        Step(3, $"Let clients reach port {server.Settings.Port}: the installer opened Windows Firewall for it; from the internet, forward it on your router.", false, "settings", "Settings");

        table.Children.Clear();
        table.RowDefinitions.Clear();
        table.ColumnDefinitions.Clear();
        string[] heads = ["Client", "User", "Address", "App", "Modules", "Since"];
        foreach (var _ in heads) table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.ColumnDefinitions[^1].Width = new GridLength(1, GridUnitType.Star);
        int row = 0;
        void Add(IEnumerable<string> cells, bool head)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int col = 0;
            foreach (var text in cells)
            {
                var t = head ? Ui.Secondary(text) : new TextBlock { Text = text };
                t.Margin = new Thickness(0, 2, 24, 4);
                Grid.SetRow(t, row);
                Grid.SetColumn(t, col++);
                table.Children.Add(t);
            }
            row++;
        }
        Add(heads, true);
        var sessions = server.Sessions().Where(s => s.Client.Length > 0).ToList();
        foreach (var s in sessions)
            Add([s.Client, s.User?.Name ?? "(signing in)", s.Address, s.App, s.Open.Count == 0 ? "—" : string.Join(", ", s.Open), s.Since.ToString("g")], false);
        if (sessions.Count == 0) Add(["Nobody yet", "", "", "", "", ""], false);
    }

    private static List<string> LocalAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .Where(a => !a.StartsWith("169.254.", StringComparison.Ordinal))
                .Distinct().ToList();
        }
        catch { return new List<string>(); }
    }

    private async Task FindPublicAsync()
    {
        publicAddress.Text = "Asking…";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            publicAddress.Text = "Public address: " + (await http.GetStringAsync("https://api.ipify.org")).Trim();
        }
        catch (Exception ex) { publicAddress.Text = "Couldn't find it: " + ex.Message; }
    }
}
