using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using DaisysServer.Modules;
using Microsoft.Win32;

namespace DaisysServer.Modules.Processes;

/// <summary>The module's page: the database's version, Export… and Import…, the PCs that sync with it (behind when
/// they haven't got the latest version), and every entry, with a search.</summary>
internal sealed class ProcessesPage : UserControl
{
    /// <summary>An entry's row.</summary>
    public sealed record Line(string Name, string Title, string Threat, string Needed, string Reviewed, string By, DateTime Updated, long Rev);

    private readonly ProcessDatabase db;
    private readonly TextBlock summary = Ui.Secondary(), result = Ui.Secondary();
    private readonly Grid pcs = new();
    private readonly TextBox search = new() { Width = 260, ToolTip = "Name, title, publisher or who changed it" };
    private readonly DataGrid grid = new() { IsReadOnly = true, MinHeight = 200 };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private List<ProcessDatabase.Row> rows = new();
    private long shownVersion = -1;

    public ProcessesPage(ProcessDatabase db)
    {
        this.db = db;

        var top = new StackPanel();
        top.Children.Add(Ui.Header("Process Analyzer's database"));
        summary.Margin = new Thickness(0, 0, 0, 10);
        top.Children.Add(summary);
        result.Margin = new Thickness(12, 0, 0, 0);
        result.VerticalAlignment = VerticalAlignment.Center;
        var export = Ui.Button("", "Export…", "Save it as Daisy's App's export file", Export);
        export.Margin = new Thickness(0, 0, 8, 0);
        top.Children.Add(Ui.Row(export, Ui.Button("", "Import…", "Merge an export into it, and send the changes to every PC", Import), result));
        var pcsHeader = Ui.Secondary("PCs");
        pcsHeader.Margin = new Thickness(0, 6, 0, 4);
        top.Children.Add(pcsHeader);
        top.Children.Add(pcs);
        var topCard = Ui.Card(top);
        topCard.Margin = new Thickness(0, 0, 0, 12);

        grid.Columns.Add(Column("Name", nameof(Line.Name)));
        grid.Columns.Add(Column("What it is", nameof(Line.Title)));
        grid.Columns.Add(Column("Threat", nameof(Line.Threat)));
        grid.Columns.Add(Column("Needed", nameof(Line.Needed)));
        grid.Columns.Add(Column("Reviewed", nameof(Line.Reviewed)));
        grid.Columns.Add(Column("By", nameof(Line.By)));
        grid.Columns.Add(Column("Updated", nameof(Line.Updated), "d MMM yyyy HH:mm"));
        grid.Columns.Add(Column("Rev", nameof(Line.Rev)));
        search.TextChanged += (_, _) => Fill();
        var searchRow = Ui.Row(Ui.Label("Search", 60), search);
        var entries = new DockPanel();
        DockPanel.SetDock(searchRow, Dock.Top);
        entries.Children.Add(searchRow);
        entries.Children.Add(grid);

        var panel = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };
        DockPanel.SetDock(topCard, Dock.Top);
        panel.Children.Add(topCard);
        panel.Children.Add(Ui.Card(entries));
        Content = panel;

        timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { Refresh(); timer.Start(); }
            else timer.Stop();
        };
    }

    private static DataGridTextColumn Column(string head, string path, string? format = null) =>
        new() { Header = head, Binding = new Binding(path) { StringFormat = format } };

    private void Refresh()
    {
        if (db.Version != shownVersion)
        {
            shownVersion = db.Version;
            rows = db.Rows();
            Fill();
        }
        summary.Text = $"Version {db.Version} · {rows.Count} programs";
        ShowPcs();
    }

    private void Fill()
    {
        string q = search.Text.Trim();
        bool Has(string s) => s.Contains(q, StringComparison.OrdinalIgnoreCase);
        grid.ItemsSource = rows.Where(r => q.Length == 0 || Has(r.Name) || Has(r.Title) || Has(r.Publisher) || Has(r.By))
            .Select(r => new Line(r.Name, r.Title, r.Threat, r.Needed, r.Reviewed ? "✓" : "", r.By, r.Updated.ToLocalTime(), r.Rev)).ToList();
    }

    private void ShowPcs()
    {
        pcs.Children.Clear();
        pcs.RowDefinitions.Clear();
        pcs.ColumnDefinitions.Clear();
        string[] heads = ["PC", "User", "App", "Version", "", "Last seen"];
        foreach (var _ in heads) pcs.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pcs.ColumnDefinitions[^1].Width = new GridLength(1, GridUnitType.Star);
        int row = 0;
        void Add(IEnumerable<string> cells, bool head)
        {
            pcs.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int col = 0;
            foreach (var text in cells)
            {
                var t = head ? Ui.Secondary(text) : new TextBlock { Text = text };
                t.Margin = new Thickness(0, 2, 24, 4);
                Grid.SetRow(t, row);
                Grid.SetColumn(t, col++);
                pcs.Children.Add(t);
            }
            row++;
        }
        Add(heads, true);
        var all = db.Pcs();
        foreach (var p in all)
            Add([p.Name, p.User, p.App, p.Version.ToString(), p.Version < db.Version ? "behind" : "up to date", p.Connected ? "connected" : Ui.Ago(p.LastSeen)], false);
        if (all.Count == 0) Add(["None yet", "", "", "", "", ""], false);
    }

    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            FileName = $"processes-{DateTime.Now:yyyy-MM-dd}.json", Filter = "Process database (*.json)|*.json", DefaultExt = ".json",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, db.Export());
            result.Text = $"Saved {db.Count} programs";
        }
        catch (Exception ex) { result.Text = "Not saved: " + ex.Message; }
    }

    private void Import()
    {
        var dialog = new OpenFileDialog { Filter = "Process database (*.json)|*.json|All files|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var (read, changed) = db.Import(File.ReadAllText(dialog.FileName));
            result.Text = $"{read} read, {changed} new or changed";
            Refresh();
        }
        catch (Exception ex) { result.Text = "Not imported: " + ex.Message; }
    }
}
