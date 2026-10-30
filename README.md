# Daisy's Server

**Version 0.1.0**

A central server for Daisy's App's clients: Daisy's App on your PCs, Daisy's App Mobile, and whatever comes next. Their
modules keep data on it and talk to each other through it, live, over the internet as well as at home.

![Icon](docs/icon.png)

- **Encrypted end to end.** A client connects with a **token** made on the server. The connection is encrypted with
  fresh keys each time, and only someone with the token can make them. Each **user's** data is sealed on their own
  clients with a key from their password, so the server keeps it but can't read it.
- **Users**: basic and admin. Each user's data is theirs alone, so one person's notes are never another's.
- **Modular**, like Daisy's App. Each feature is a module (`modules\<Name>\`) that the server hands its clients'
  calls to. A client says which of the server's modules it wants, and from which of its own. Either side can switch a
  module off.
- **First module: Notepad.** Daisy's App's Tools → Notepad notes, in named tabs, for each user. Typed on any of their
  clients, they show on all of them at once.
- **A window, not a service.** It runs in the tray, starts with Windows if you like, and updates itself.

The protocol is in [PROTOCOL.md](PROTOCOL.md).

## Install

Run `DaisysServer-Setup-x.y.z.exe`. Windows asks for admin approval, because it installs to
`C:\Program Files\Daisys Server` and lets the server through Windows Firewall. The installer isn't signed, so
SmartScreen may warn you: choose **More info → Run anyway**. Everything it needs is included.

## Setting it up

1. **Users**: add one for each person. Make yourself an admin. A user's password makes the key their data is sealed
   with, and each user can change it from a client (Daisy's App: Settings → Tools → Notepad → Daisy's Server →
   Change password).
   - Setting a new password here, on the server, **deletes that user's data**, since it can't be opened without the
     old one.
2. **Tokens**: make one for each person or device and send it to them. **Revoke** shuts it out and drops its
   connections. **Copy** copies it again whenever you need it.
3. **Reaching it**:
   - At home: clients use this PC's address (shown on **Status**) and the port (38900 unless you change it in
     **Settings**).
   - From the internet: forward that TCP port on your router to this PC, and use your public address (**Show public
     address** on Status) or a DNS name for it.
4. **In Daisy's App** (3.25.1 or later): Settings → Tools → Notepad → **Daisy's Server**. Fill in the address, port,
   token, user and password, then **Connect**. Then pick **Daisy's Server** in the Notepad's host box. The tabs you
   already had go up to the server the first time.

## The window

| Tab | |
|---|---|
| **Status** | Listening or not (**Start** / **Stop**), the addresses clients use, the steps to get going while there are no users or tokens yet, and who's connected: client, user, address, app, the modules it's using, and since when. |
| **Users** | Add a user (name, password, admin). For each one: **Make admin / Make basic**, **New password…** (which deletes their data), and **Delete** (a two-click **Confirm?**). |
| **Tokens** | **Make token** (named for who it's for), **Copy**, and **Revoke** (**Confirm?**). |
| *a tab per module* | **Notepad**: each user's notes as kept (how many, their size, the changes since the last snapshot, when last changed) and the clients they're joined from. What the notes say isn't shown, because the server can't read it. |
| **Log** | The server's and its modules' log, live. **Open folder** opens the files. |
| **Settings** | **Server**: the name clients see, the port, and listening at start. **Startup and tray**: start with Windows (in the tray), and keep running in the tray when the window closes. **Updates**: the version, check when it starts, **Check for updates**, and **Release notes**. **Modules**: each one on or off, from the next start (**Restart now**). **Files**: the folders below. |

Closing the window keeps it running in the tray. Right-click the tray icon for Open, Start / Stop listening, Check for
updates, or Exit. Only one copy runs at a time: starting it again shows the running one.

## Updates

The server checks for updates when it starts (if that's on) and when you click **Check for updates** in Settings (or
the tray). It reads the latest [GitHub release](https://github.com/rosscarlson/DaisysServer/releases), as Daisy's App
does. When there's a newer one, the bar at the top offers **What's new** and **Install update**, and nothing installs
until you click.

The installer is downloaded, checked against the SHA-256 GitHub publishes for it, and run silently (Windows asks for
admin approval). The server then starts again on the new version. Its users, tokens and data are kept.

## Files and command line

| | |
|---|---|
| Program | `C:\Program Files\Daisys Server\DaisysServer.exe`, with each module in `modules\<Name>\` |
| Settings, users, tokens | `%APPDATA%\DaisysServer\` (`settings.json`, `users.json`, `tokens.json`; tokens' secrets DPAPI-encrypted for your Windows user) |
| Users' data | `%LOCALAPPDATA%\DaisysServer\data\users\<user id>\<module>\` (sealed by the clients) |
| Logs | `%LOCALAPPDATA%\DaisysServer\logs\`, a file a day, kept 14 days |
| Start with Windows | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `DaisysServer` |
| Firewall | the rule *Daisy's Server* (made by the installer, removed by the uninstaller) |

| Argument | |
|---|---|
| *(none)* | Start, or show the running copy |
| `--tray` | Start in the tray (used by start with Windows) |
| `--exit` | Ask the running copy to close (used by the uninstaller) |
| `--restart` | Wait for the running copy to close, then start (**Restart now**) |

`DAISYSSERVER_HOME=<folder>` puts everything it keeps in that folder instead (settings, users, tokens, data and
logs), and the copy runs beside the real one. Use it for trying a build.

## Project layout

```
src/
  Directory.Build.props     version, .NET 8, WPF
  DaisysServer.Core/        what the server and modules share:
    Protocol/               the wire (Wire.cs: token, handshake, frames, Vault) and the client (ServerClient.cs);
                            the same two files are in Daisy's App's src/DaisysApp.Core/Server/
    Modules/                IServerModule + [ServerModule], IModuleChannel, IModuleHost, ModuleLog, JsonFile, Ui
  DaisysServer/             the server: App (single instance, tray, startup), MainWindow (tabs, bar, updates),
                            Server/ (ServerHost: listener and modules; Session: one connection; UserStore, TokenStore,
                            ModuleCatalog, ServerSettings), Pages/ (Status, Users, Tokens, Log, Settings),
                            Updates/UpdateService, Themes/ (Daisy's App's look)
  Modules/
    Directory.Build.props   makes each folder here a module (DaisysServer.<Name>.dll in modules\<Name>\)
    Notepad/                NotepadModule (the module), Hub (one user's notes and clients), NotepadPage (its tab)
installer/DaisysServer.iss  Inno Setup
tools/
  TestClient/               a console client: end-to-end checks, `setup` (a test server's user and token), `poke`
  make_icon.py              draws the icon
```

### Writing a module

Make a folder in `src/Modules/<Name>/` with a `<Name>.csproj` (`<Project Sdk="Microsoft.NET.Sdk" />` is enough; the
folder's `Directory.Build.props` does the rest). In it, write one class that implements `IServerModule`, marked
`[ServerModule("id", "Name", Description = "…", Icon = "\uE…")]`:

- `Start(host)`: once, at start. `host` gives it its log (`host.Log`), a folder per user (`host.FolderFor(user)`), and
  the users.
- `ServeAsync(channel, ct)`: one client's channel, until it ends. Read with `channel.ReadAsync`, which returns null when
  it's over, and send with `channel.Post`. `channel.User`, `channel.Client` and `channel.From` say who it is.
- `View`: its tab in the window, built with `Ui` (cards, buttons, Confirm?). Return null for none.
- `UserRemoved(user)`: the user was deleted, or their password was reset. Forget them.

Seal anything a user owns on the client, with `ServerChannel.DataKey`. The server should only order, keep and pass
messages on. Add the module's messages to PROTOCOL.md. In Daisy's App, the module opens its channel with Core's
`ServerLink.OpenAsync("id", "its own id", ct)`.

## Build and release

Requires the .NET 8 SDK and Inno Setup 6.

```powershell
dotnet build DaisysServer.sln   # the server, its modules (in src\DaisysServer\bin\...\modules) and TestClient
.\build.ps1                     # -> artifacts\DaisysServer-Setup-<version>.exe
```

To try a build: run `TestClient setup <folder> <user> <password>` to make a test user and print a token. Then start
`DaisysServer.exe` with `DAISYSSERVER_HOME=<folder>` (put `{"Port": 38991}` in its `settings.json`) and run
`TestClient 127.0.0.1 38991 <token> <user> <password>`.

**To release:**

1. Bump `<Version>` in `src/Directory.Build.props` and the version at the top of this file.
2. Run `.\build.ps1`.
3. Make a GitHub release on `rosscarlson/DaisysServer`, tagged `vX.Y.Z`, with `DaisysServer-Setup-X.Y.Z.exe` attached.
   Installed servers update from it.

## Third-party

.NET 8 runtime (bundled), Inno Setup (the installer), [ipify](https://www.ipify.org) (only when you click Show public
address).
