using System.Windows;
using System.Windows.Controls;
using DaisysServer.Modules;
using DaisysServer.Server;

namespace DaisysServer.Pages;

/// <summary>
/// Users: add one (user name, password, the profile fields, admin), and for each one: Edit (name and profile), admin
/// or basic, a new password (which deletes their data: it's sealed with a key from the old one), and Delete. The
/// profile's boxes come from <see cref="UserStore.Fields"/>. Buttons are icons, their tooltips saying what they do.
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
        var profile = Boxes(null);
        var admin = new CheckBox { Content = "Admin (can manage users from a client too)", Margin = new Thickness(130, 0, 0, 8), IsChecked = !server.Users.Any() };
        var add = Ui.Button("", "", "Add user", () =>
        {
            var problem = server.Users.Add(name.Text, password.Password, admin.IsChecked == true, Values(profile));
            Show(problem ?? $"{name.Text.Trim()} added.", problem != null);
            if (problem == null)
            {
                name.Clear();
                password.Clear();
                foreach (var box in profile.Values) box.Clear();
                admin.IsChecked = false;
            }
        }, accent: true);
        result.Margin = new Thickness(12, 0, 0, 0);
        result.VerticalAlignment = VerticalAlignment.Center;
        var form = new StackPanel();
        form.Children.Add(Ui.Header("Add a user"));
        form.Children.Add(Ui.Row(Ui.Label("User name"), name));
        form.Children.Add(Ui.Row(Ui.Label("Password"), password));
        foreach (var f in UserStore.Fields) form.Children.Add(Ui.Row(Ui.Label(f.Label), profile[f.Key]));
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

    /// <summary>A box for each profile field, filled from the user's (or empty).</summary>
    private static Dictionary<string, TextBox> Boxes(UserStore.User? u) =>
        UserStore.Fields.ToDictionary(f => f.Key, f =>
        {
            var box = new TextBox { Width = 220, MaxLength = f.Max, Text = u?.Get(f.Key) ?? "", ToolTip = f.Tip };
            System.Windows.Automation.AutomationProperties.SetName(box, f.Label);
            return box;
        });

    private static Dictionary<string, string> Values(Dictionary<string, TextBox> boxes) => boxes.ToDictionary(kv => kv.Key, kv => kv.Value.Text);

    private static T Spaced<T>(T e) where T : FrameworkElement
    {
        e.Margin = new Thickness(6, 0, 0, 0);
        return e;
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
            string shown = u.DisplayName != u.Name ? $"{u.DisplayName} ({u.Name})" : u.Name;
            who.Children.Add(new TextBlock { Text = shown + (u.Admin ? "  ·  admin" : ""), FontWeight = FontWeights.SemiBold });
            string email = u.Get("email") is { Length: > 0 } e ? e + " · " : "";
            who.Children.Add(Ui.Secondary($"{email}Added {u.Created.ToLocalTime():d MMM yyyy} · last signed in {Ui.Ago(u.LastSignIn)}{(u.Vault == null ? " · not signed in yet" : "")}"));

            var edit = Panel();
            var reset = Panel();
            void Toggle(StackPanel open, StackPanel other)
            {
                other.Visibility = Visibility.Collapsed;
                open.Visibility = open.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            }
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(Ui.Button("", "", "Edit name and details", () => Toggle(edit, reset)));
            actions.Children.Add(Spaced(Ui.Button(u.Admin ? "" : "", "", u.Admin ? "Make basic" : "Make admin", () => Show(server.Users.SetAdmin(u.Id, !u.Admin)))));
            actions.Children.Add(Spaced(Ui.Button("", "", "New password…", () => Toggle(reset, edit))));
            actions.Children.Add(Spaced(Ui.Confirm("", "", "Delete the user and everything kept for them", () => Show(server.Users.Delete(u.Id)))));
            DockPanel.SetDock(actions, Dock.Right);
            line.Children.Add(actions);
            line.Children.Add(who);
            row.Children.Add(line);

            var newName = new TextBox { Width = 220, Text = u.Name, ToolTip = "What they sign in with" };
            System.Windows.Automation.AutomationProperties.SetName(newName, "User name");
            var boxes = Boxes(u);
            edit.Children.Add(Ui.Row(Ui.Label("User name"), newName));
            foreach (var f in UserStore.Fields) edit.Children.Add(Ui.Row(Ui.Label(f.Label), boxes[f.Key]));
            var editProblem = Ui.Secondary();
            editProblem.SetResourceReference(TextBlock.ForegroundProperty, "ErrorTextBrush");
            editProblem.VerticalAlignment = VerticalAlignment.Center;
            editProblem.Margin = new Thickness(12, 0, 0, 0);
            var save = Ui.Button("", "", "Save", () => editProblem.Text = server.Users.Edit(u.Id, newName.Text, Values(boxes)) ?? "", accent: true);
            var saveRow = Ui.Row(save, editProblem);
            saveRow.Margin = new Thickness(130, 0, 0, 0);
            edit.Children.Add(saveRow);
            row.Children.Add(edit);

            var newPassword = new PasswordBox { Width = 200, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(newPassword, "New password");
            var set = Ui.Confirm("", "", "Set password: their data is deleted (it's sealed with a key from the old password)", () => Show(server.Users.ResetPassword(u.Id, newPassword.Password)));
            set.Margin = new Thickness(8, 0, 12, 0);
            var warn = Ui.Secondary("Deletes their data: it's sealed with their old password. To keep it, they change it from a client.");
            warn.VerticalAlignment = VerticalAlignment.Center;
            warn.MaxWidth = 380;
            var resetRow = new StackPanel { Orientation = Orientation.Horizontal };
            resetRow.Children.Add(newPassword);
            resetRow.Children.Add(set);
            resetRow.Children.Add(warn);
            reset.Children.Add(resetRow);
            row.Children.Add(reset);
            list.Children.Add(row);
        }
    }

    private static StackPanel Panel() => new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };

    private void Show(string? problem) => Show(problem ?? "", problem != null);

    private void Show(string text, bool error)
    {
        result.Text = text;
        result.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorTextBrush" : "TextSecondaryBrush");
    }
}
