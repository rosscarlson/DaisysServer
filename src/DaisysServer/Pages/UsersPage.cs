using System.Windows;
using System.Windows.Controls;
using DaisysServer.Modules;
using DaisysServer.Server;

namespace DaisysServer.Pages;

/// <summary>
/// Users. One form adds a user (user name, password twice, the profile fields, groups) or, after a row's Edit, edits
/// one: Update saves it (a new password deletes their data, so then it's a two-click Confirm?), and Add, once something's
/// changed, makes a new user from it. Each row: Edit, Delete. The profile's boxes come from <see cref="UserStore.Fields"/>,
/// the groups from <see cref="UserStore.Groups"/>. Buttons are icons, their tooltips saying what they do; no help text
/// (the README).
/// </summary>
internal sealed class UsersPage : UserControl
{
    // the two password boxes side by side, everything else as wide as both
    private const double Half = 146, Gap = 8, Wide = Half * 2 + Gap;
    private readonly ServerHost server;
    private readonly StackPanel list = new();
    private readonly TextBlock header = Ui.Header("Add a user"), result = Ui.Secondary();
    private readonly TextBox name = new() { Width = Wide };
    private readonly PasswordBox password = new() { Width = Half }, confirm = new() { Width = Half, Margin = new Thickness(Gap, 0, 0, 0) };
    private readonly Dictionary<string, TextBox> profile;
    private readonly MultiPick groups = new(UserStore.Groups, "Groups") { Width = Wide };
    private readonly Button add, update, updateReset, cancel;
    private readonly FrameworkElement form;
    private UserStore.User? editing;

    public UsersPage(ServerHost server)
    {
        this.server = server;
        profile = UserStore.Fields.ToDictionary(f => f.Key, f =>
        {
            var box = new TextBox { Width = Wide, MaxLength = f.Max, ToolTip = f.Tip };
            System.Windows.Automation.AutomationProperties.SetName(box, f.Label);
            box.TextChanged += (_, _) => Buttons();
            return box;
        });
        System.Windows.Automation.AutomationProperties.SetName(name, "User name");
        System.Windows.Automation.AutomationProperties.SetName(password, "Password");
        System.Windows.Automation.AutomationProperties.SetName(confirm, "Password again");
        password.ToolTip = "Editing, empty keeps it";
        confirm.ToolTip = "The password again";
        name.TextChanged += (_, _) => Buttons();
        password.PasswordChanged += (_, _) => Buttons();
        confirm.PasswordChanged += (_, _) => Buttons();
        groups.Changed += Buttons;

        add = Ui.Button("", "", "Add user", AddUser, accent: true);
        update = Ui.Button("", "", "Update", () => Update(), accent: true);
        updateReset = Ui.Confirm("", "", "Update, with the new password: their data is deleted", () => Update());
        cancel = Ui.Button("", "", "Stop editing", Clear);
        foreach (var b in new[] { update, updateReset, cancel }) b.Margin = new Thickness(8, 0, 0, 0);
        result.Margin = new Thickness(12, 0, 0, 0);
        result.VerticalAlignment = VerticalAlignment.Center;

        var fields = new StackPanel();
        fields.Children.Add(header);
        fields.Children.Add(Ui.Row(Ui.Label("User name"), name));
        fields.Children.Add(Ui.Row(Ui.Label("Password"), password, confirm));
        foreach (var f in UserStore.Fields) fields.Children.Add(Ui.Row(Ui.Label(f.Label), profile[f.Key]));
        fields.Children.Add(Ui.Row(Ui.Label("Groups"), groups));
        var buttons = Ui.Row(add, update, updateReset, cancel, result);
        buttons.Margin = new Thickness(130, 0, 0, 0);
        fields.Children.Add(buttons);
        form = Ui.Card(fields);

        var users = new StackPanel();
        users.Children.Add(Ui.Header("Users"));
        users.Children.Add(list);
        Content = Ui.Page(form, Ui.Card(users));
        server.Users.Changed += () => Dispatcher.BeginInvoke(Refresh);
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
        Clear();
        Refresh();
    }

    private Dictionary<string, string> Values() => profile.ToDictionary(kv => kv.Key, kv => kv.Value.Text);

    /// <summary>Whether the form differs from the user being edited.</summary>
    private bool Changed() => editing is { } u && (name.Text.Trim() != u.Name || !groups.Selected.SequenceEqual(u.Groups) || password.Password.Length > 0
        || UserStore.Fields.Any(f => profile[f.Key].Text.Trim() != u.Get(f.Key)));

    private void Buttons()
    {
        bool edit = editing != null, changed = Changed();
        add.IsEnabled = !edit || changed;
        // (while editing, Update is the main one)
        if (edit) add.ClearValue(StyleProperty);
        else add.SetResourceReference(StyleProperty, "AccentButton");
        update.Visibility = edit && password.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        updateReset.Visibility = edit && password.Password.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        update.IsEnabled = updateReset.IsEnabled = changed;
        cancel.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The form back to adding, empty.</summary>
    private void Clear()
    {
        editing = null;
        header.Text = "Add a user";
        name.Clear();
        password.Clear();
        confirm.Clear();
        foreach (var box in profile.Values) box.Clear();
        // the first user is the admin
        groups.Selected = server.Users.Any() ? [UserStore.UserGroup] : [UserStore.AdminGroup, UserStore.UserGroup];
        Buttons();
    }

    private void Edit(UserStore.User u)
    {
        editing = u;
        header.Text = $"Edit {u.Name}";
        name.Text = u.Name;
        password.Clear();
        confirm.Clear();
        foreach (var f in UserStore.Fields) profile[f.Key].Text = u.Get(f.Key);
        groups.Selected = u.Groups;
        Show("", false);
        Buttons();
        form.BringIntoView();
        name.Focus();
    }

    private string? Mismatch() => password.Password != confirm.Password ? "The passwords don't match." : null;

    private void AddUser()
    {
        string? problem = Mismatch() ?? server.Users.Add(name.Text, password.Password, false, Values(), groups.Selected);
        if (problem != null) { Show(problem, true); return; }
        string added = name.Text.Trim();
        Clear();
        Show($"{added} added.", false);
    }

    private void Update()
    {
        if (editing is not { } u) return;
        string? problem = Mismatch() ?? server.Users.Edit(u.Id, name.Text, Values());
        if (problem == null && !groups.Selected.SequenceEqual(u.Groups)) problem = server.Users.SetGroups(u.Id, groups.Selected);
        if (problem == null && password.Password.Length > 0) problem = server.Users.ResetPassword(u.Id, password.Password);
        if (problem != null) { Show(problem, true); return; }
        string updated = name.Text.Trim();
        Clear();
        Show($"{updated} updated.", false);
    }

    private static T Spaced<T>(T e) where T : FrameworkElement
    {
        e.Margin = new Thickness(6, 0, 0, 0);
        return e;
    }

    private void Refresh()
    {
        list.Children.Clear();
        var all = server.Users.All().OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (editing != null && all.All(u => u.Id != editing.Id)) Clear();
        if (all.Count == 0) list.Children.Add(Ui.Secondary("No users yet."));
        foreach (var u in all)
        {
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            string shown = u.DisplayName != u.Name ? $"{u.DisplayName} ({u.Name})" : u.Name;
            who.Children.Add(new TextBlock { Text = shown + (u.Groups.Count > 0 ? "  ·  " + string.Join(", ", u.Groups) : ""), FontWeight = FontWeights.SemiBold });
            string email = u.Get("email") is { Length: > 0 } e ? e + " · " : "";
            who.Children.Add(Ui.Secondary($"{email}Added {u.Created.ToLocalTime():d MMM yyyy} · last signed in {Ui.Ago(u.LastSignIn)}"));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(Ui.Button("", "", "Edit", () => Edit(u)));
            actions.Children.Add(Spaced(Ui.Confirm("", "", "Delete the user and everything kept for them", () => Show(server.Users.Delete(u.Id)))));
            DockPanel.SetDock(actions, Dock.Right);
            line.Children.Add(actions);
            line.Children.Add(who);
            list.Children.Add(line);
        }
        // the user being edited, as they are now
        if (editing is { } ed && all.FirstOrDefault(u => u.Id == ed.Id) is { } fresh) editing = fresh;
        Buttons();
    }

    private void Show(string? problem) => Show(problem ?? "", problem != null);

    private void Show(string text, bool error)
    {
        result.Text = text;
        result.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorTextBrush" : "TextSecondaryBrush");
    }
}
