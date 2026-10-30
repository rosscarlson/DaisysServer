using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DaisysServer.Modules;

namespace DaisysServer.Modules.Notepad;

/// <summary>The module's page: each user's notes as kept (how many, their size, changes since the last snapshot,
/// when last changed) and the clients they have joined from. What the notes say is sealed: it isn't shown.</summary>
internal sealed class NotepadPage : UserControl
{
    private readonly NotepadModule module;
    private readonly Grid table = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };

    public NotepadPage(NotepadModule module, IModuleHost host)
    {
        this.module = module;
        var card = new StackPanel();
        card.Children.Add(Ui.Header("Notes by user"));
        var about = Ui.Secondary("Sealed on each user's clients with their own key: the server keeps them but can't read them.");
        about.Margin = new Thickness(0, 0, 0, 10);
        card.Children.Add(about);
        card.Children.Add(table);
        Content = Ui.Page(Ui.Card(card));
        timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { Refresh(); timer.Start(); }
            else timer.Stop();
        };
    }

    private void Refresh()
    {
        table.Children.Clear();
        table.RowDefinitions.Clear();
        table.ColumnDefinitions.Clear();
        string[] heads = ["User", "Notes", "Size", "Changes since snapshot", "Last change", "Connected from"];
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
        var all = module.Summaries().ToList();
        foreach (var (user, s) in all)
            Add([user.Name, s.Notes.ToString(), Ui.Size(s.Bytes), s.Changes.ToString(), Ui.Ago(s.Changed), s.Clients.Count == 0 ? "—" : string.Join(", ", s.Clients)], false);
        if (all.Count == 0) Add(["No users yet", "", "", "", "", ""], false);
    }
}
