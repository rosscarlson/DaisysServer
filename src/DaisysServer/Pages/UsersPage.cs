using System.Windows;
using System.Windows.Controls;
using DaisysServer.Modules;
using DaisysServer.Server;

namespace DaisysServer.Pages;

/// <summary>
/// Users and their tokens. One form adds a user (user name, password twice, the profile fields, admin; a token is made
/// with them and copied) or, after a row's Edit, edits one: Update saves it (a new password deletes their data, so then
/// it's a two-click Confirm?), and Add, once something's changed, makes a new user from it. Each row: Edit, New token,
/// Delete, and the user's tokens (hidden; Copy, Revoke). Tokens made before 0.2.2 are for anyone: tie them to a user.
/// The profile's boxes come from <see cref="UserStore.Fields"/>. Buttons are icons, their tooltips saying what they do.
/// </summary>
internal sealed class UsersPage : UserControl
{
    private readonly ServerHost server;
    private readonly StackPanel list = new(), anyone = new();
    private readonly Border anyoneCard;
    private readonly TextBlock header = Ui.Header("Add a user"), result = Ui.Secondary(), passwordNote = Ui.Secondary("Empty keeps it. A new one deletes their data: it's sealed with their old password (to keep it, they change it from a client).");
    private readonly TextBox name = new() { Width = 220 };
    private readonly PasswordBox password = new() { Width = 220 }, confirm = new() { Width = 220 };
    private readonly Dictionary<string, TextBox> profile;
    private readonly CheckBox admin = new() { Content = "Admin (can manage users from a client too)", Margin = new Thickness(130, 0, 0, 8) };
    private readonly Button add, update, updateReset, cancel;
    private readonly FrameworkElement form;
    private UserStore.User? editing;

    public UsersPage(ServerHost server)
    {
        this.server = server;
        admin.IsChecked = !server.Users.Any();
        profile = UserStore.Fields.ToDictionary(f => f.Key, f =>
        {
            var box = new TextBox { Width = 220, MaxLength = f.Max, ToolTip = f.Tip };
            System.Windows.Automation.AutomationProperties.SetName(box, f.Label);
            box.TextChanged += (_, _) => Buttons();
            return box;
        });
        System.Windows.Automation.AutomationProperties.SetName(name, "User name");
        System.Windows.Automation.AutomationProperties.SetName(password, "Password");
        System.Windows.Automation.AutomationProperties.SetName(confirm, "Password again");
        confirm.ToolTip = "The password again";
        name.TextChanged += (_, _) => Buttons();
        password.PasswordChanged += (_, _) => Buttons();
        confirm.PasswordChanged += (_, _) => Buttons();
        admin.Checked += (_, _) => Buttons();
        admin.Unchecked += (_, _) => Buttons();

        add = Ui.Button("", "", "Add user (a token is made with them, and copied)", AddUser, accent: true);
        update = Ui.Button("", "", "Update", () => Update(), accent: true);
        updateReset = Ui.Confirm("", "", "Update, with the new password: their data is deleted", () => Update());
        cancel = Ui.Button("", "", "Stop editing", Clear);
        foreach (var b in new[] { update, updateReset, cancel }) b.Margin = new Thickness(8, 0, 0, 0);
        result.Margin = new Thickness(12, 0, 0, 0);
        result.VerticalAlignment = VerticalAlignment.Center;
        passwordNote.Margin = new Thickness(130, -4, 0, 8);
        passwordNote.TextWrapping = TextWrapping.Wrap;
        passwordNote.MaxWidth = 520;
        passwordNote.HorizontalAlignment = HorizontalAlignment.Left;

        var fields = new StackPanel();
        fields.Children.Add(header);
        fields.Children.Add(Ui.Row(Ui.Label("User name"), name));
        var again = Ui.Label("Again", 50);
        again.Margin = new Thickness(12, 0, 0, 0);
        fields.Children.Add(Ui.Row(Ui.Label("Password"), password, again, confirm));
        fields.Children.Add(passwordNote);
        foreach (var f in UserStore.Fields) fields.Children.Add(Ui.Row(Ui.Label(f.Label), profile[f.Key]));
        fields.Children.Add(admin);
        var buttons = Ui.Row(add, update, updateReset, cancel, result);
        buttons.Margin = new Thickness(130, 0, 0, 0);
        fields.Children.Add(buttons);
        var note = Ui.Secondary("A client needs this server's address and port, the user's token (to connect: only they can sign in with it), " +
                                "and their name and password. The password makes the key their data is sealed with, on their clients: the server never has it.");
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(0, 6, 0, 0);
        fields.Children.Add(note);
        form = Ui.Card(fields);

        var users = new StackPanel();
        users.Children.Add(Ui.Header("Users"));
        users.Children.Add(list);
        var older = new StackPanel();
        older.Children.Add(Ui.Header("Tokens for anyone"));
        var olderNote = Ui.Secondary("Made before tokens belonged to users: any user can sign in with these. Tie each to its user, or revoke it.");
        olderNote.Margin = new Thickness(0, 0, 0, 8);
        older.Children.Add(olderNote);
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
    private bool Changed() => editing is { } u && (name.Text.Trim() != u.Name || (admin.IsChecked == true) != u.Admin || password.Password.Length > 0
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
        cancel.Visibility = passwordNote.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
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
        admin.IsChecked = !server.Users.Any();
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
        admin.IsChecked = u.Admin;
        Show("", false);
        Buttons();
        form.BringIntoView();
        name.Focus();
    }

    private string? Mismatch() => password.Password != confirm.Password ? "The passwords don't match." : null;

    private void AddUser()
    {
        string? problem = Mismatch() ?? server.Users.Add(name.Text, password.Password, admin.IsChecked == true, Values());
        if (problem != null) { Show(problem, true); return; }
        string added = name.Text.Trim();
        var u = server.Users.All().First(x => x.Name.Equals(added, StringComparison.OrdinalIgnoreCase));
        Copy(server.Tokens.Create(u.Name, u.Id));
        Clear();
        Show($"{added} added. Their token is copied, to paste into their client.", false);
    }

    private void Update()
    {
        if (editing is not { } u) return;
        string? problem = Mismatch() ?? server.Users.Edit(u.Id, name.Text, Values());
        if (problem == null && (admin.IsChecked == true) != u.Admin) problem = server.Users.SetAdmin(u.Id, admin.IsChecked == true);
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

    /// <summary>A token's line: hidden, when it was made and last used, and its buttons (Copy, Revoke, and any more).</summary>
    private DockPanel TokenLine(TokenStore.Entry t, string label, params FrameworkElement[] more)
    {
        var line = new DockPanel { Margin = new Thickness(16, 4, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var m in more) actions.Children.Add(Spaced(m));
        actions.Children.Add(Spaced(Ui.Button("", "", "Copy the token, to paste into their client", () => { Copy(server.Tokens.Text(t.Id)); Show("Token copied.", false); })));
        actions.Children.Add(Spaced(Ui.Confirm("", "", "Revoke: nobody can connect with it any more; its connections are dropped", () => server.Tokens.Revoke(t.Id))));
        DockPanel.SetDock(actions, Dock.Right);
        line.Children.Add(actions);
        var about = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var glyph = Ui.Glyph("", 12);
        glyph.Margin = new Thickness(0, 0, 8, 0);
        about.Children.Add(glyph);
        about.Children.Add(new TextBlock { Text = "••••••••••••", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        about.Children.Add(Ui.Secondary($"{label}made {t.Created.ToLocalTime():d MMM yyyy} · last used {Ui.Ago(t.LastUsed)}"));
        line.Children.Add(about);
        return line;
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
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var line = new DockPanel();
            var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            string shown = u.DisplayName != u.Name ? $"{u.DisplayName} ({u.Name})" : u.Name;
            who.Children.Add(new TextBlock { Text = shown + (u.Admin ? "  ·  admin" : ""), FontWeight = FontWeights.SemiBold });
            string email = u.Get("email") is { Length: > 0 } e ? e + " · " : "";
            who.Children.Add(Ui.Secondary($"{email}Added {u.Created.ToLocalTime():d MMM yyyy} · last signed in {Ui.Ago(u.LastSignIn)}{(u.Vault == null ? " · not signed in yet" : "")}"));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(Ui.Button("", "", "Edit", () => Edit(u)));
            actions.Children.Add(Spaced(Ui.Button("", "", "New token for them (copied)", () =>
            {
                Copy(server.Tokens.Create(u.Name, u.Id));
                Show($"A new token for {u.Name} is copied.", false);
            })));
            actions.Children.Add(Spaced(Ui.Confirm("", "", "Delete the user, their tokens and everything kept for them", () => Show(server.Users.Delete(u.Id)))));
            DockPanel.SetDock(actions, Dock.Right);
            line.Children.Add(actions);
            line.Children.Add(who);
            row.Children.Add(line);
            var theirs = tokens.Where(t => t.UserId == u.Id).ToList();
            foreach (var t in theirs) row.Children.Add(TokenLine(t, ""));
            if (theirs.Count == 0)
            {
                var none = Ui.Secondary(tokens.Any(t => t.UserId.Length == 0) ? "No token of their own: they connect with a token for anyone (below)" : "No token: they can't connect");
                none.Margin = new Thickness(16, 4, 0, 0);
                row.Children.Add(none);
            }
            list.Children.Add(row);
        }

        anyone.Children.Clear();
        var loose = tokens.Where(t => t.UserId.Length == 0).ToList();
        anyoneCard.Visibility = loose.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var t in loose)
        {
            var tie = new ComboBox { Width = 160, ItemsSource = all, DisplayMemberPath = nameof(UserStore.User.Name), ToolTip = "Tie it to a user: only they can sign in with it" };
            System.Windows.Automation.AutomationProperties.SetName(tie, "Tie to");
            tie.SelectionChanged += (_, _) => { if (tie.SelectedItem is UserStore.User u) server.Tokens.Assign(t.Id, u.Id, u.Name); };
            var line = TokenLine(t, $"{t.Name} · ", tie);
            line.Margin = new Thickness(0, 4, 0, 0);
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
