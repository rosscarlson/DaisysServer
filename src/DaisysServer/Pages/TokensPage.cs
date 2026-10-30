using System.Windows;
using System.Windows.Controls;
using DaisysServer.Modules;
using DaisysServer.Server;

namespace DaisysServer.Pages;

/// <summary>
/// Tokens: what a client needs to connect at all (with the address and port, then a user's name and password). Make
/// one per person or device, copy it to them, and revoke it to shut it out (its connections are dropped).
/// </summary>
internal sealed class TokensPage : UserControl
{
    private readonly ServerHost server;
    private readonly StackPanel list = new();

    public TokensPage(ServerHost server)
    {
        this.server = server;
        var name = new TextBox { Width = 220 };
        var made = new TextBox { IsReadOnly = true, Width = 520, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Visibility = Visibility.Collapsed };
        var copy = Ui.Button("", "Copy", null, () => Copy(made.Text));
        copy.Visibility = Visibility.Collapsed;
        copy.Margin = new Thickness(8, 0, 0, 0);
        var make = Ui.Button("", "Make token", null, () =>
        {
            made.Text = server.Tokens.Create(name.Text);
            made.Visibility = copy.Visibility = Visibility.Visible;
            name.Clear();
            made.Focus();
            made.SelectAll();
        }, accent: true);
        make.Margin = new Thickness(8, 0, 0, 0);
        var form = new StackPanel();
        form.Children.Add(Ui.Header("Make a token"));
        form.Children.Add(Ui.Row(Ui.Label("Name (who it's for)"), name, make));
        form.Children.Add(Ui.Row(made, copy));
        var note = Ui.Secondary("A client needs this server's address, its port and a token to connect, then a user's name and password to sign in. " +
                                "The connection is encrypted with keys only someone with the token can make; a new key each time.");
        form.Children.Add(note);

        var tokens = new StackPanel();
        tokens.Children.Add(Ui.Header("Tokens"));
        tokens.Children.Add(list);
        Content = Ui.Page(Ui.Card(form), Ui.Card(tokens));
        server.Tokens.Changed += () => Dispatcher.BeginInvoke(Refresh);
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
        Refresh();
    }

    private static void Copy(string text)
    {
        try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.ExternalException) { }
    }

    private void Refresh()
    {
        list.Children.Clear();
        var all = server.Tokens.All();
        if (all.Count == 0) list.Children.Add(Ui.Secondary("No tokens yet: nobody can connect."));
        foreach (var t in all)
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var about = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            about.Children.Add(new TextBlock { Text = t.Name, FontWeight = FontWeights.SemiBold });
            about.Children.Add(Ui.Secondary($"{t.Id} · made {t.Created.ToLocalTime():d MMM yyyy} · last used {Ui.Ago(t.LastUsed)}"));
            var copy = Ui.Button("", "Copy", "Copies the token, to paste into a client", () => { if (server.Tokens.Text(t.Id) is { } text) Copy(text); });
            var revoke = Ui.Confirm("", "Revoke", "Nobody can connect with it any more; its connections are dropped", () => server.Tokens.Revoke(t.Id));
            revoke.Margin = new Thickness(8, 0, 0, 0);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { copy, revoke } };
            DockPanel.SetDock(actions, Dock.Right);
            line.Children.Add(actions);
            line.Children.Add(about);
            list.Children.Add(line);
        }
    }
}
