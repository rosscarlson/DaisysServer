using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using DaisysServer.Modules;

namespace DaisysServer.Modules.Sessions;

/// <summary>The module's page: every session shared, searched in every column, picked one or many to remove or to ban
/// whoever shared them (their user, PC, address or token: banning removes all of theirs); and the bans.</summary>
internal sealed class SessionsPage : UserControl
{
    /// <summary>A session's row.</summary>
    public sealed record Line(string Id, DateTime Shared, string Title, string Description, string Driver, string Game, string Track, string Car,
        string Class, int Laps, string Best, string User, string Client, string Address, string Token, string Size, string Video, SessionStore.Row Row);

    private readonly SessionStore store;
    private readonly Func<IReadOnlyList<ServerUser>> users;
    private readonly TextBlock summary = Ui.Secondary(), result = Ui.Secondary(), picked = Ui.Secondary();
    private readonly TextBox search = new() { Width = 300, ToolTip = "Words anywhere in a row: title, driver, track, user, PC, address…" };
    private readonly DataGrid grid = new() { IsReadOnly = true, MinHeight = 260, SelectionMode = DataGridSelectionMode.Extended, AutoGenerateColumns = false };
    private readonly Grid banList = new();
    private readonly ComboBox banKind = new() { Width = 100, ItemsSource = new[] { "User", "PC", "Address" }, SelectedIndex = 2 };
    private readonly TextBox banValue = new() { Width = 220, ToolTip = "A user's name, a PC's name, or an address (or a range: 203.0.113.0/24)" };
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private List<SessionStore.Row> rows = new();

    public SessionsPage(SessionStore store, Func<IReadOnlyList<ServerUser>> users)
    {
        this.store = store;
        this.users = users;

        var top = new StackPanel();
        top.Children.Add(Ui.Header("Shared sessions"));
        summary.Margin = new Thickness(0, 0, 0, 10);
        top.Children.Add(summary);
        System.Windows.Automation.AutomationProperties.SetName(search, "Search");
        System.Windows.Automation.AutomationProperties.SetName(banValue, "Who to ban");
        search.TextChanged += (_, _) => Fill();
        picked.VerticalAlignment = VerticalAlignment.Center;
        picked.Margin = new Thickness(12, 0, 0, 0);
        top.Children.Add(Ui.Row(Ui.Label("Search", 60), search, picked));
        var actions = Ui.Row(
            Spaced(Ui.Confirm("", "Remove", "Take the sessions picked off the server", RemovePicked)),
            Spaced(Ui.Confirm("", "Ban user", "Stop the picked sessions' users sharing, and remove all of theirs", () => BanPicked(SessionStore.BanKind.User))),
            Spaced(Ui.Confirm("", "Ban PC", "Stop the picked sessions' PCs sharing, and remove all of theirs", () => BanPicked(SessionStore.BanKind.PC))),
            Spaced(Ui.Confirm("", "Ban address", "Stop the picked sessions' IP addresses sharing, and remove all of theirs", () => BanPicked(SessionStore.BanKind.Address))),
            Spaced(Ui.Confirm("", "Ban token", "Stop the picked sessions' tokens sharing, and remove all of theirs", () => BanPicked(SessionStore.BanKind.Token))),
            result);
        result.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(actions);

        grid.Columns.Add(Column("Shared", nameof(Line.Shared), "d MMM yyyy HH:mm"));
        grid.Columns.Add(Column("Title", nameof(Line.Title)));
        grid.Columns.Add(Column("Driver", nameof(Line.Driver)));
        grid.Columns.Add(Column("Game", nameof(Line.Game)));
        grid.Columns.Add(Column("Track", nameof(Line.Track)));
        grid.Columns.Add(Column("Car", nameof(Line.Car)));
        grid.Columns.Add(Column("Class", nameof(Line.Class)));
        grid.Columns.Add(Column("Laps", nameof(Line.Laps)));
        grid.Columns.Add(Column("Best", nameof(Line.Best)));
        grid.Columns.Add(Column("User", nameof(Line.User)));
        grid.Columns.Add(Column("PC", nameof(Line.Client)));
        grid.Columns.Add(Column("Address", nameof(Line.Address)));
        grid.Columns.Add(Column("Token", nameof(Line.Token)));
        grid.Columns.Add(Column("Size", nameof(Line.Size)));
        grid.Columns.Add(Column("Video", nameof(Line.Video)));
        grid.SelectionChanged += (_, _) => picked.Text = grid.SelectedItems.Count > 0 ? $"{grid.SelectedItems.Count} picked" : "";
        var list = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        list.Children.Add(top);
        list.Children.Add(grid);

        var bans = new StackPanel();
        bans.Children.Add(Ui.Header("Banned from sharing"));
        banList.Margin = new Thickness(0, 0, 0, 8);
        bans.Children.Add(banList);
        banKind.Margin = new Thickness(0, 0, 8, 0);
        banValue.Margin = new Thickness(0, 0, 8, 0);
        bans.Children.Add(Ui.Row(banKind, banValue, Ui.Confirm("", "Ban", "Stop them sharing, and remove all of theirs", BanTyped)));
        var bansCard = Ui.Card(bans);
        bansCard.Margin = new Thickness(0, 12, 0, 0);

        var panel = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };
        DockPanel.SetDock(bansCard, Dock.Bottom);
        panel.Children.Add(bansCard);
        panel.Children.Add(Ui.Card(list));
        Content = panel;

        debounce.Tick += (_, _) => { debounce.Stop(); Refresh(); };
        store.Changed += () => Dispatcher.BeginInvoke(() => { if (IsVisible) debounce.Start(); });
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
    }

    private static T Spaced<T>(T b) where T : FrameworkElement
    {
        b.Margin = new Thickness(0, 0, 8, 0);
        return b;
    }

    private static DataGridTextColumn Column(string head, string path, string? format = null) =>
        new() { Header = head, Binding = new Binding(path) { StringFormat = format } };

    private void Refresh()
    {
        rows = store.Rows();
        summary.Text = $"{rows.Count} sessions · {Ui.Size(store.TotalSize)}";
        Fill();
        ShowBans();
    }

    private void Fill()
    {
        var keep = grid.SelectedItems.OfType<Line>().Select(l => l.Id).ToHashSet();
        string[] words = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lines = rows.Select(r => new Line(r.Id, r.Shared, r.Title, r.Description, r.Driver, r.Game, r.Track, r.Car, r.Class, r.Laps,
                r.Best > 0 ? TimeSpan.FromSeconds(r.Best).ToString(@"m\:ss\.fff") : "", r.User, r.Client, r.Address, r.Token, Ui.Size(r.Size), r.Video, r))
            .Where(l =>
            {
                if (words.Length == 0) return true;
                string all = string.Join(" ", l.Shared.ToString("d MMM yyyy HH:mm"), l.Title, l.Description, l.Driver, l.Game, l.Track, l.Car, l.Class,
                    l.Laps, l.Best, l.User, l.Client, l.Address, l.Token, l.Video, l.Id);
                return words.All(w => all.Contains(w, StringComparison.OrdinalIgnoreCase));
            }).ToList();
        grid.ItemsSource = lines;
        foreach (var l in lines.Where(l => keep.Contains(l.Id))) grid.SelectedItems.Add(l);
    }

    private List<SessionStore.Row> Picked() => grid.SelectedItems.OfType<Line>().Select(l => l.Row).ToList();

    private void RemovePicked()
    {
        var p = Picked();
        if (p.Count == 0) { result.Text = "Pick sessions first"; return; }
        result.Text = $"{store.Remove(p.Select(r => r.Id), "An admin on the server")} removed";
        Refresh();
    }

    private void BanPicked(SessionStore.BanKind kind)
    {
        var p = Picked();
        if (p.Count == 0) { result.Text = "Pick sessions first"; return; }
        var who = p.Select(r => kind switch
            {
                SessionStore.BanKind.User => (Value: r.UserId, Name: r.User),
                SessionStore.BanKind.PC => (Value: r.Client, Name: r.Client),
                SessionStore.BanKind.Token => (Value: r.TokenId, Name: r.Token.Length > 0 ? r.Token : r.TokenId),
                _ => (Value: r.Address, Name: r.Address),
            }).Where(w => w.Value.Length > 0).DistinctBy(w => w.Value).ToList();
        int removed = who.Sum(w => store.AddBan(kind, w.Value, w.Name, ""));
        result.Text = $"{who.Count} banned, {removed} sessions removed";
        Refresh();
    }

    private void BanTyped()
    {
        string value = banValue.Text.Trim();
        if (value.Length == 0) return;
        var kind = (SessionStore.BanKind)banKind.SelectedIndex;
        string name = value;
        if (kind == SessionStore.BanKind.User)
        {
            if (users().FirstOrDefault(u => string.Equals(u.Name, value, StringComparison.OrdinalIgnoreCase)) is not { } u) { result.Text = $"No user called {value}"; return; }
            (value, name) = (u.Id, u.Name);
        }
        else if (kind == SessionStore.BanKind.Address && !SessionStore.ValidAddress(value)) { result.Text = $"{value} isn't an address or a range"; return; }
        result.Text = $"{name} banned, {store.AddBan(kind, value, name, "")} sessions removed";
        banValue.Clear();
        Refresh();
    }

    private void ShowBans()
    {
        banList.Children.Clear();
        banList.RowDefinitions.Clear();
        banList.ColumnDefinitions.Clear();
        for (int i = 0; i < 4; i++) banList.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var all = store.BanList();
        int row = 0;
        void Add(UIElement e, int col)
        {
            if (e is FrameworkElement f) f.Margin = new Thickness(0, 2, 24, 4);
            Grid.SetRow(e, row);
            Grid.SetColumn(e, col);
            banList.Children.Add(e);
        }
        foreach (var b in all)
        {
            banList.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Add(Ui.Secondary(b.Kind.ToString()), 0);
            Add(new TextBlock { Text = b.Name, VerticalAlignment = VerticalAlignment.Center }, 1);
            Add(Ui.Secondary(Ui.Ago(b.At)), 2);
            var ban = b;
            Add(Ui.Button("", "Unban", "Let them share again", () => { store.Unban(ban); Refresh(); }), 3);
            row++;
        }
        if (all.Count == 0)
        {
            banList.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Add(Ui.Secondary("None"), 0);
        }
    }
}
