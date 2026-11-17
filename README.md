# Daisy's Server

**Version 0.3.0**

A central server for Daisy's App's clients: Daisy's App on your PCs, Daisy's App Mobile, and whatever comes next. Their
modules keep data on it and talk to each other through it, live, over the internet as well as at home.

![Icon](docs/icon.png)

- **Encrypted end to end.** A client knows the server by its **key** (shown on Status; built into Daisy's App, or
  trusted the first time) and connects only to the server that has it. The connection is encrypted with fresh keys
  each time that only that server can make. Users sign in with their name and password; 5 wrong ones from an address
  in 15 minutes and it can't sign in for 15 minutes. Each **user's** data is sealed on their own clients with a key
  from their password, so the server keeps it but can't read it. **Signing in is optional** for the modules that
  allow it (Sessions): anonymous clients are known by their PC's name and address.
- **Users**: in groups (Admin, User, SE-Analysis; Admin manages users). Each user's data is theirs alone, so one person's notes are never another's.
- **Modular**, like Daisy's App. Each feature is a module (`modules\<Name>\`) that the server hands its clients'
  calls to. A client says which of the server's modules it wants, and from which of its own. Either side can switch a
  module off.
- **Notepad.** Daisy's App's Tools → Notepad notes, in named tabs, for each user. Typed on any of their clients, they
  show on all of them at once.
- **Files** (since 0.1.1). This PC's drives, the ones you pick in **Settings → Shared drives**, for a client (Daisy's
  App Mobile) to browse, preview and open. Read-only: nothing on them can be changed, moved or deleted from a client.
  Admins only, unless you tick **Basic users too**.
- **Processes** (since 0.1.2). The master database for Daisy's App's Support → Process Analyzer: the programs its
  clients know and how they're classified. Each client sends its changes and gets everyone else's. One database for
  every user, and not sealed (programs' names and classifications aren't anyone's data, and the server merges them).
- **Sessions** (since 0.2.0). Daisy's App's SimEffects → Analysis → Share Session (4.17.0 or later): racing sessions
  shared for every user to browse, download and compare. Not sealed (everyone reads them). On the **Sessions** tab an
  admin finds sessions, removes them one at a time or many at once, and bans whoever shared one from sharing again
  (their user, PC or address; an anonymous one's install), which removes all of theirs. Anyone may share without
  signing in (since 0.3.0): their sessions show their PC's name.
- **A window, not a service.** It runs in the tray, starts with Windows if you like, and updates itself.

The protocol is in [PROTOCOL.md](PROTOCOL.md).

## Install

Run `DaisysServer-Setup-x.y.z.exe`. Windows asks for admin approval, because it installs to
`C:\Program Files\Daisys Server` and lets the server through Windows Firewall. The installer isn't signed, so
SmartScreen may warn you: choose **More info → Run anyway**. Everything it needs is included.

## Setting it up

1. **Users**: add one for each person. Put yourself in **Admin**. A user's password makes the key their data is sealed
   with, and each user can change it from a client (Daisy's App: Settings → General → Daisy's Server → Change
   password).
   - Setting a new password here, on the server, **deletes that user's data**, since it can't be opened without the
     old one.
2. **The server's key**: **Status** shows it (**Copy**). Daisy's App has yours built in, or trusts a server the
   first time it connects and refuses one with another key after. It's kept in `settings.json`: keep that file (back
   it up), because a new key means every client has to forget the old one. (Tokens went in 0.3.0: updating deletes
   `tokens.json`.)
3. **Reaching it**:
   - At home: clients use this PC's address (shown on **Status**) and the port (38900 unless you change it in
     **Settings**).
   - Windows Firewall: the installer lets the server through (a rule for the program, whatever port it's on).
   - From the internet: forward that TCP port on your router to this PC, and use your public address (**Show public
     address** on Status) or a DNS name for it.
4. **In Daisy's App** (a version for 0.3.0: older ones can't connect to it): Settings → General → **Daisy's Server**.
   Fill in the address, port, user and password (or none: Sessions only), then **Connect**. Tools → Notepad then keeps its notes here; the tabs you already had go up to
   the server the first time.
5. **Files**: tick the drives clients may browse in **Settings → Shared drives**. None are shared until you do.

## The window

| Tab | |
|---|---|
| **Status** | Listening or not (**Start** / **Stop**), the addresses clients use, the server's **key** (**Copy**), the steps to get going while there are no users yet, and who's connected: client, user (or anonymous), address, app, the modules it's using, and since when. |
| **Users** | One form: **Add a user** (user name, the password twice, display name, email, **Groups**: a dropdown of checkboxes, a new user in User). A user's **Edit** fills it in as **Edit &lt;name&gt;**: **Update** saves it (renaming keeps their password and data, but their clients must sign in with the new name; a password typed there is a new one, which deletes their data, so Update then asks **Confirm?**), **Add** (gray until something's changed) makes a new user from it, and **✕** stops editing. Each user (with their groups), as icons (point at one for what it does): **Edit** and **Delete** (a two-click **Confirm?**). The display name and email are kept on the server as written: they're how you know who's who, not sealed data. |
| *a tab per module* | **Notepad**: each user's notes as kept (how many, their size, the changes since the last snapshot, when last changed) and the clients they're joined from. What the notes say isn't shown, because the server can't read it. **Processes**: the database's version, **Export…** and **Import…** (Daisy's App's export file; an import goes to every PC), the PCs (behind or up to date), and every program, with a search. **Sessions**: every session shared (when, title, driver, game, track, car, class, laps, best, user (or *(anonymous)*), PC, address, size, video) with their count and total size, a **Search** that looks for each word anywhere in a row, and, for the sessions picked (Ctrl+click or Shift+click for many): **Remove**, **Ban user** (an anonymous one: their install), **Ban PC**, **Ban address** (each **Confirm?**; a ban removes every session it matches). Under it, **Banned from sharing**: each ban with **Unban**, and a box to ban a user, PC or address (or a range, like `203.0.113.0/24`) by hand. Someone banned can still browse and download. (**Files** has no tab: its settings are in Settings, and the **Log** shows each file opened.) |
| **Log** | The server's and its modules' log, live. **Open folder** opens the files. |
| **Settings** | **Server**: the name clients see, the port, and listening at start. **Startup and tray**: start with Windows (in the tray), and keep running in the tray when the window closes (on unless you untick it: then the window's ✕ exits). **Updates**: the version, check when it starts, **Check for updates**, and **Release notes**. **Modules**: each one on or off, from the next start (**Restart now**). **Shared drives (Files)**: a box for each drive (with its free space), ticked to share it, and **Basic users too**. **Folders**: the folders below. |

Closing the window keeps it running in the tray. Right-click the tray icon for Open, Start / Stop listening, Check for
updates, or Exit. Only one copy runs at a time: starting it again shows the running one.

## Updates

The server checks for updates when it starts (if that's on) and when you click **Check for updates** in Settings (or
the tray). It reads the latest [GitHub release](https://github.com/rosscarlson/DaisysServer/releases), as Daisy's App
does. When there's a newer one, the bar at the top offers **What's new** and **Install update**, and nothing installs
until you click.

The installer is downloaded, checked against the SHA-256 GitHub publishes for it, and run silently (Windows asks for
admin approval). The server then starts again on the new version. Its users, key and data are kept.

## Files and command line

| | |
|---|---|
| Program | `C:\Program Files\Daisys Server\DaisysServer.exe`, with each module in `modules\<Name>\` |
| Settings, users | `%APPDATA%\DaisysServer\` (`settings.json`, with the server's key: back it up; `users.json`; `files.json`, the shared drives) |
| Users' data | `%LOCALAPPDATA%\DaisysServer\data\users\<user id>\<module>\` (sealed by the clients) |
| Process database | `%LOCALAPPDATA%\DaisysServer\data\processes\` (`processes.json`, `clients.json`: one for everyone, not sealed) |
| Shared sessions | `%LOCALAPPDATA%\DaisysServer\data\sessions\` (`sessions.json`, a `<id>.gz` each, `bans.json`: one for everyone, not sealed) |
| Logs | `%LOCALAPPDATA%\DaisysServer\logs\`, a file a day, kept 14 days |
| Start with Windows | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `DaisysServer` |
| Firewall | the rule *Daisy's Server* (made by the installer, removed by the uninstaller) |

| Argument | |
|---|---|
| *(none)* | Start, or show the running copy |
| `--tray` | Start in the tray (used by start with Windows) |
| `--exit` | Ask the running copy to close (used by the uninstaller) |
| `--restart` | Wait for the running copy to close, then start (**Restart now**) |

`DAISYSSERVER_HOME=<folder>` puts everything it keeps in that folder instead (settings, users, data and
logs), and the copy runs beside the real one. Use it for trying a build.

## Project layout

```
src/
  Directory.Build.props     version, .NET 8, WPF
  DaisysServer.Core/        what the server and modules share:
    Protocol/               the wire (Wire.cs: server key, handshake, frames, Vault) and the client (ServerClient.cs);
                            the same two files are in Daisy's App's src/DaisysApp.Core/Server/
    Modules/                IServerModule + [ServerModule], IModuleChannel, IModuleHost, ModuleLog, JsonFile, Ui, MultiPick
  DaisysServer/             the server: App (single instance, tray, startup), MainWindow (tabs, bar, updates),
                            Server/ (ServerHost: listener and modules; Session: one connection; UserStore, SignInLimit,
                            ModuleCatalog, ServerSettings), Pages/ (Status, Users, Log, Settings),
                            Updates/UpdateService, Themes/ (Daisy's App's look)
  Modules/
    Directory.Build.props   makes each folder here a module (DaisysServer.<Name>.dll in modules\<Name>\)
    Notepad/                NotepadModule (the module), Hub (one user's notes and clients), NotepadPage (its tab)
    Files/                  FilesModule (the module: the questions, paths checked), FilesSettings (files.json),
                            FilesSettingsView (its card in Settings), Thumbnails (Windows' own)
    Processes/              ProcessesModule (the module), ProcessDatabase (entries, merge, PCs, usage), ProcessesPage
    Sessions/               SessionsModule (the module: its questions), SessionStore (index, files, uploads, bans,
                            pushes), SessionsPage (its tab: search, remove, ban)
installer/DaisysServer.iss  Inno Setup
tools/
  TestClient/               a console client: end-to-end checks, `setup` (a test server's settings with its port and key, its users, and a shared drive; prints the key), `poke`, `report` (a processes entry), `share` (a session, from a PC you name; an empty user shares anonymously)
  make_icon.py              draws the icon
```

### Writing a module

Make a folder in `src/Modules/<Name>/` with a `<Name>.csproj` (`<Project Sdk="Microsoft.NET.Sdk" />` is enough; the
folder's `Directory.Build.props` does the rest). In it, write one class that implements `IServerModule`, marked
`[ServerModule("id", "Name", Description = "…", Icon = "\uE…")]`:

- `Start(host)`: once, at start. `host` gives it its log (`host.Log`), a folder per user (`host.FolderFor(user)`), and
  the users.
- `ServeAsync(channel, ct)`: one client's channel, until it ends. Read with `channel.ReadAsync`, which returns null when
  it's over, and send with `channel.Post`. `channel.User`, `channel.Client` and `channel.From` say who it is (and since 0.2.0 `channel.Address` where it came from;
  since 0.3.0 `channel.User.Anonymous` for a client that didn't sign in).
- `View`: its tab in the window, built with `Ui` (cards, buttons, Confirm?). Return null for none.
- `SettingsView` (optional): its card in Settings, after Modules. Settings save as they change.
- `UserRemoved(user)`: the user was deleted, or their password was reset. Forget them.
- `Anonymous` (optional, since 0.3.0): true lets clients that haven't signed in open channels to it.

Seal anything a user owns on the client, with `ServerChannel.DataKey` (Files serves the server's own files, and
Processes and Sessions keep what everyone shares: those three aren't sealed). The server should only order, keep and pass
messages on. Add the module's messages to PROTOCOL.md. In Daisy's App, the module opens its channel with Core's
`ServerLink.OpenAsync("id", "its own id", ct)`.

## Build and release

Requires the .NET 8 SDK and Inno Setup 6.

```powershell
dotnet build DaisysServer.sln   # the server, its modules (in src\DaisysServer\bin\...\modules) and TestClient
.\build.ps1                     # -> artifacts\DaisysServer-Setup-<version>.exe
```

To try a build: run `TestClient setup <folder> <user> <password>` to make a test server's settings (port 38991, a key)
and users, and print its key. Then start `DaisysServer.exe` with `DAISYSSERVER_HOME=<folder>` and run
`TestClient 127.0.0.1 38991 <key> <user> <password>`. Its last checks lock 127.0.0.1 out of signing in for 15 minutes:
restart the test server to run it again.

**To release:**

1. Bump `<Version>` in `src/Directory.Build.props` and the version at the top of this file.
2. Run `.\build.ps1`.
3. Make a GitHub release on `rosscarlson/DaisysServer`, tagged `vX.Y.Z`, with `DaisysServer-Setup-X.Y.Z.exe` attached.
   Installed servers update from it.

## Third-party

.NET 8 runtime (bundled), Inno Setup (the installer), [ipify](https://www.ipify.org) (only when you click Show public
address).
