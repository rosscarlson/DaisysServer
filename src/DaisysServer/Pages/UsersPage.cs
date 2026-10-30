using System.Windows;
using System.Windows.Controls;
using DaisysServer.Modules;
using DaisysServer.Server;

namespace DaisysServer.Pages;

/// <summary>
/// Users: add one (name, password, admin), and for each one: admin or basic, a new password (which deletes their data:
/// it's sealed with a key from the old one), and Delete.
/// </summary>
internal sealed class UsersPage : UserControl
{
    private readonly ServerHost server;
    private readonly StackPanel list = new();
    private readonly TextBlock result = Ui.Secondary();

    public UsersPage(ServerHost server)
    {
        this.server = server;
        var name = new TextBox { Width = 220 };
        var password = new PasswordBox { Width = 220 };
        var admin = new CheckBox { Content = "Admin (can manage users from a client too)", Margin = new Thickness(130, 0, 0, 8), IsChecked = !server.Users.Any() };
        var add = Ui.Button("", "Add user", null, () =>
        {
            var problem = server.Users.Add(name.Text, password.Password, admin.IsChecked == true);
            result.Text = problem ?? $"{name.Text.Trim()} added.";
            result.SetResourceReference(TextBlock.ForegroundProperty, problem != null ? "ErrorTextBrush" : "TextSecondaryBrush");
            if (problem == null)
            {
                name.Clear();
                password.Clear();
                admin.IsChecked = false;
            }
        }, accent: true);
        result.Margin = new Thickness(12, 0, 0, 0);
        result.VerticalAlignment = VerticalAlignment.Center;
        var form = new StackPanel();
        form.Children.Add(Ui.Header("Add a user"));
        form.Children.Add(Ui.Row(Ui.Label("Name"), name));
        form.Children.Add(Ui.Row(Ui.Label("Password"), password));
        form.Children.Add(admin);
        var buttons = Ui.Row(add, result);
        buttons.Margin = new Thickness(130, 0, 0, 0);
        form.Children.Add(buttons);
        var note = Ui.Secondary("The password makes the key each user's data is sealed with, on their clients: the server never has it. Each user can change it from a client.");
        note.Margin = new Thickness(0, 6, 0, 0);
        form.Children.Add(note);

        var users = new StackPanel();
        users.Children.Add(Ui.Header("Users"));
        users.Children.Add(list);
        Content = Ui.Page(Ui.Card(form), Ui.Card(users));
        server.Users.Changed += () => Dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    private void Refresh()
    {
        list.Children.Clear();
        var all = server.Users.All().OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (all.Count == 0) list.Children.Add(Ui.Secondary("No users yet."));
        foreach (var u in all)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var line = new DockPanel();
            var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            who.Children.Add(new TextBlock { Text = u.Name + (u.Admin ? "  ·  admin" : ""), FontWeight = FontWeights.SemiBold });
            who.Children.Add(Ui.Secondary($"Added {u.Created.ToLocalTime():d MMM yyyy} · last signed in {Ui.Ago(u.LastSignIn)}{(u.Vault == null ? " · not signed in yet" : "")}"));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var reset = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
            var role = Ui.Button(u.Admin ? "" : "", u.Admin ? "Make basic" : "Make admin", null, () => Show(server.Users.SetAdmin(u.Id, !u.Admin)));
            var pw = Ui.Button("", "New password…", null, () => reset.Visibility = reset.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible);
            var delete = Ui.Confirm("", "Delete", "Deletes the user and everything kept for them", () => Show(server.Users.Delete(u.Id)));
            pw.Margin = delete.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(role);
            actions.Children.Add(pw);
            actions.Children.Add(delete);
            DockPanel.SetDock(actions, Dock.Right);
            line.Children.Add(actions);
            line.Children.Add(who);
            row.Children.Add(line);

            var newPassword = new PasswordBox { Width = 200, VerticalAlignment = VerticalAlignment.Center };
            var set = Ui.Confirm("", "Set password", "Their data is deleted: it's sealed with a key from the old password", () => Show(server.Users.ResetPassword(u.Id, newPassword.Password)));
            set.Margin = new Thickness(8, 0, 12, 0);
            var warn = Ui.Secondary("Deletes their data: it's sealed with their old password. To keep it, they change it from a client.");
            warn.VerticalAlignment = VerticalAlignment.Center;
            warn.MaxWidth = 380;
            reset.Children.Add(newPassword);
            reset.Children.Add(set);
            reset.Children.Add(warn);
            row.Children.Add(reset);
            list.Children.Add(row);
        }
    }

    private void Show(string? problem)
    {
        result.Text = problem ?? "";
        result.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
    }
}
