using System.Windows;
using System.Windows.Controls;
using DaisysServer.Modules;
using DaisysServer.Server;

namespace DaisysServer.Pages;

/// <summary>
/// Users and their tokens. One form adds a user (user name, password twice, the profile fields, groups; a token is made
/// with them and copied) or, after a row's Edit, edits one: Update saves it (a new password deletes their data, so then
/// it's a two-click Confirm?), and Add, once something's changed, makes a new user from it. Editing, the Token row has
/// their token hidden, Copy, and Replace (or New token when they've none). Each row: Edit, Delete. Tokens made before
/// 0.2.2 are for anyone: tie them to a user. The profile's boxes come from <see cref="UserStore.Fields"/>, the groups
/// from <see cref="UserStore.Groups"/>. Buttons are icons, their tooltips saying what they do; no help text (the README).
/// </summary>
internal sealed class UsersPage : UserControl
{
    // the two password boxes side by side, everything else as wide as both
    private const double Half = 146, Gap = 8, Wide = Half * 2 + Gap;
    private readonly ServerHost server;
    private readonly StackPanel list = new(), anyone = new();
    private readonly Border anyoneCard;
    private readonly TextBlock header = Ui.Header("Add a user"), result = Ui.Secondary();
    private readonly TextBox name = new() { Width = Wide };
    private readonly PasswordBox password = new() { Width = Half }, confirm = new() { Width = Half, Margin = new Thickness(Gap, 0, 0, 0) };
    private readonly Dictionary<string, TextBox> profile;
    private readonly MultiPick groups = new(UserStore.Groups, "Groups") { Width = Wide };
    private readonly TextBox token = new() { IsReadOnly = true, IsTabStop = false };
    private readonly Button add, update, updateReset, cancel, copyToken, replaceToken, newToken;
    private readonly FrameworkElement form, tokenRow;
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
        System.Windows.Automation.AutomationProperties.SetName(token, "Token");
        password.ToolTip = "Editing, empty keeps it";
        confirm.ToolTip = "The password again";
        name.TextChanged += (_, _) => Buttons();
        password.PasswordChanged += (_, _) => Buttons();
        confirm.PasswordChanged += (_, _) => Buttons();
        groups.Changed += Buttons;

        add = Ui.Button("", "", "Add user (their token is copied)", AddUser, accent: true);
        update = Ui.Button("", "", "Update", () => Update(), accent: true);
        updateReset = Ui.Confirm("", "", "Update, with the new password: their data is deleted", () => Update());
        cancel = Ui.Button("", "", "Stop editing", Clear);
        foreach (var b in new[] { update, updateReset, cancel }) b.Margin = new Thickness(8, 0, 0, 0);
        result.Margin = new Thickness(12, 0, 0, 0);
        result.VerticalAlignment = VerticalAlignment.Center;

        copyToken = Ui.Button("", "", "Copy the token", () => { if (editing is { } u) { Copy(Newest(u)); Show("Token copied.", false); } });
        replaceToken = Ui.Confirm("", "", "Replace the token (copied): the old one stops working", () => { if (editing is { } u) MakeToken(u, true); });
        newToken = Ui.Button("", "", "Make a token (copied)", () => { if (editing is { } u) MakeToken(u, false); });
        var tokenLine = new DockPanel { Width = Wide };
        foreach (var b in new[] { newToken, replaceToken, copyToken })
        {
            b.Margin = new Thickness(Gap, 0, 0, 0);
            DockPanel.SetDock(b, Dock.Right);
            tokenLine.Children.Add(b);
        }
        tokenLine.Children.Add(token);

        var fields = new StackPanel();
        fields.Children.Add(header);
        fields.Children.Add(Ui.Row(Ui.Label("User name"), name));
        fields.Children.Add(Ui.Row(Ui.Label("Password"), password, confirm));
        foreach (var f in UserStore.Fields) fields.Children.Add(Ui.Row(Ui.Label(f.Label), profile[f.Key]));
        fields.Children.Add(tokenRow = Ui.Row(Ui.Label("Token"), tokenLine));
        fields.Children.Add(Ui.Row(Ui.Label("Groups"), groups));
        var buttons = Ui.Row(add, update, updateReset, cancel, result);
        buttons.Margin = new Thickness(130, 0, 0, 0);
        fields.Children.Add(buttons);
        form = Ui.Card(fields);

        var users = new StackPanel();
        users.Children.Add(Ui.Header("Users"));
        users.Children.Add(list);
        var older = new StackPanel();
        older.Children.Add(Ui.Header("Tokens for anyone"));
        older.Children.Add(anyone);
        anyoneCard = Ui.Card(older);
        Content = Ui.Page(form, Ui.Card(users), anyoneCard);
        server.Users.Changed += () => Dispatcher.BeginInvoke(Refresh);
        server.Tokens.Changed += () => Dispatcher.BeginInvoke(Refresh);
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
        cancel.Visibility = tokenRow.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
        if (editing is not { } u) return;
        bool has = Newest(u) != null;
        token.Text = has ? new string('•', 24) : "";
        copyToken.IsEnabled = has;
        replaceToken.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        newToken.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The user's newest token, or null.</summary>
    private string? Newest(UserStore.User u) =>
        server.Tokens.All().Where(t => t.UserId == u.Id).OrderByDescending(t => t.Created).FirstOrDefault() is { } t ? server.Tokens.Text(t.Id) : null;

    /// <summary>A token for them, copied; replacing, their others are revoked first.</summary>
    private void MakeToken(UserStore.User u, bool replace)
    {
        if (replace) server.Tokens.RevokeFor(u.Id);
        Copy(server.Tokens.Create(u.Name, u.Id));
        Show(replace ? "Token replaced and copied." : "Token made and copied.", false);
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
        var u = server.Users.All().First(x => x.Name.Equals(added, StringComparison.OrdinalIgnoreCase));
        Copy(server.Tokens.Create(u.Name, u.Id));
        Clear();
        Show($"{added} added; token copied.", false);
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

    private static void Copy(string? text)
    {
        if (text == null) return;
        try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.ExternalException) { }
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
        var tokens = server.Tokens.All();
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
            actions.Children.Add(Spaced(Ui.Confirm("", "", "Delete the user, their tokens and everything kept for them", () => Show(server.Users.Delete(u.Id)))));
            DockPanel.SetDock(actions, Dock.Right);
            line.Children.Add(actions);
            line.Children.Add(who);
            list.Children.Add(line);
        }
        // the user being edited: their token may have changed
        if (editing is { } ed && all.FirstOrDefault(u => u.Id == ed.Id) is { } fresh) editing = fresh;
        Buttons();

        anyone.Children.Clear();
        var loose = tokens.Where(t => t.UserId.Length == 0).ToList();
        anyoneCard.Visibility = loose.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var t in loose)
        {
            var line = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var tie = new ComboBox { Width = 160, ItemsSource = all, DisplayMemberPath = nameof(UserStore.User.Name), ToolTip = "Tie it to a user" };
            System.Windows.Automation.AutomationProperties.SetName(tie, "Tie to");
            tie.SelectionChanged += (_, _) => { if (tie.SelectedItem is UserStore.User u) server.Tokens.Assign(t.Id, u.Id, u.Name); };
            actions.Children.Add(tie);
            actions.Children.Add(Spaced(Ui.Button("", "", "Copy the token", () => { Copy(server.Tokens.Text(t.Id)); Show("Token copied.", false); })));
            actions.Children.Add(Spaced(Ui.Confirm("", "", "Revoke", () => server.Tokens.Revoke(t.Id))));
            DockPanel.SetDock(actions, Dock.Right);
            line.Children.Add(actions);
            line.Children.Add(new TextBlock { Text = $"{t.Name} · ••••••••••••", VerticalAlignment = VerticalAlignment.Center });
            anyone.Children.Add(line);
        }
    }

    private void Show(string? problem) => Show(problem ?? "", problem != null);

    private void Show(string text, bool error)
    {
        result.Text = text;
        result.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorTextBrush" : "TextSecondaryBrush");
    }
}
