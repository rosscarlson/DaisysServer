# Daisy's Server

The server for Daisy's App's clients (D:\OneDrive\Code\Windows\DaisysApp, Daisy's App Mobile
D:\OneDrive\Code\Android\DaisysApp). `README.md` covers the app, `PROTOCOL.md` the wire (the one description of it).
Module notes are in each module's own CLAUDE.md (`src/Modules/Notepad/CLAUDE.md`).

## Rules

- **`src/DaisysServer.Core/Protocol/Wire.cs` and `ServerClient.cs` also live in Daisy's App**, at
  `src/DaisysApp.Core/Server/`. Keep the two copies identical (copy them over after a change). They must stay free
  of WPF and other dependencies (TestClient links them on net8.0).
- Ops are only ever added, never changed. Put each op in PROTOCOL.md, with the version it came in.
- The server never sees users' plaintext. A module stores what clients seal, so a feature that needs the server to
  read data is a design question for the user first.
- UI text: labels and short tooltips; the README explains. Things that can't be undone use the two-click
  `Ui.Confirm`. Settings save as they change.
- **Testing never touches the real server's files.** Run with `DAISYSSERVER_HOME=%TEMP%\DaisysServerTest`, and make
  the user and token with `TestClient setup`. For Daisy's App, make throwaway copies (`%TEMP%\dsedit\make_dev.ps1`
  made `DaisysDevS`/`DaisysDevS2` with only the Tools module), then delete them and their
  `%APPDATA%`/`%LOCALAPPDATA%` folders afterwards. **Never stop DaisysApp.exe**: sessions run in it.
- Heredocs with long C# break the Bash tool here: write files with Write, and edit scripts to a file in `%TEMP%\dsedit`.

## Session notes

**Asked and done** (all released on `rosscarlson/DaisysServer`, public; the releases are the updater's source):
- 2026-10-28/30: this server (0.1.0) with Notepad and DA's side (DA 3.26.4/3.26.5: Settings → General card, Notepad on
  the server only); **Files** (0.1.1; `src/Modules/Files/CLAUDE.md`); Android briefs in Downloads
  (`DaisysServer-Android-Brief.md`, `DaisysServer-Files-Android-Brief.md`).
- 2026-11-04: **Processes** from DA's brief (0.1.2; `src/Modules/Processes/CLAUDE.md`).
- 2026-11-13: **Sessions** from DA 4.17.0's brief (`docs/BRIEF-sessions-module.md`, Share Session) plus an admin tab
  to find sessions, remove one or many, and ban a cheater's user, PC, IP or token; the port; the next minor. Released
  as **0.2.0** (2026-11-13; `src/Modules/Sessions/CLAUDE.md`). Changed: `src/Modules/Sessions/` (new),
  `IModuleChannel` (`Address`, `TokenId`, `TokenName`, default members) and `Session.cs`, PROTOCOL §7, README, the
  version, TestClient (`sessions:` checks, `share`; `setup` also makes `<user>-basic` and two bans). Told the user:
  port **38900** TCP (forward it on the router; the installer's firewall rule is for the program, any port); DA needs
  nothing.
- 2026-11-14: **0.2.1** (released): users' display name and email, fields from one list (`UserStore.Fields`: a new
  field is one line; the page, `users.json`'s `Profile` and the ops follow it), op `useredit`, icon-only buttons on
  Users (`Ui.Button` with empty text), the version in the title. Asked how sign-in is secured: answered from below.
- 2026-11-14, later: one form on Users for Add and Edit (Edit fills it as "Edit <name>"; Update, or Confirm? when a
  new password's typed; Add gray until something's changed, to make a new user from one; ✕ stops editing), the
  password twice on one line, and **tokens merged into Users**. Released as **0.2.2**. Changed: `UsersPage.cs` (rewritten: form, each user's tokens hidden
  with Copy/Revoke, New token, "Tokens for anyone" to tie old ones), `TokensPage.cs` (deleted; tab gone),
  `TokenStore.cs` (`UserId`, `UserOf`, `Assign`, `RevokeFor`), `UserStore.cs` (`Deleted` event, `ToString`),
  `ServerHost.cs` (delete revokes their tokens), `Session.cs` (login refuses another user's token; `useradd` answers
  with `token`; op `usertoken`), `StatusPage.cs` step 2, `MainWindow.cs`, README, PROTOCOL §2/§3, the version,
  TestClient (5 more `users:` checks).
- DA's side lives in DA (`src/DaisysApp.Core/Server/`, details in DA's `src/Modules/Tools/CLAUDE.md`). There, DA's
  `*-GAMER11.*`/`*-PLEX*` files are OneDrive conflict copies and other sessions' work is left alone: commit only
  your own files. DA's local clone may be behind GitHub (other PCs release): `git fetch`/`git show origin/main:…`.

**Decisions (mine, told to the user):**
- **Connecting:** token `dsk1_` + 6-byte id + 32-byte secret; ECDH P-256 + HKDF salted with the secret (forward
  secrecy, mutual proof). **Signing in:** PBKDF2 makes the sign-in proof and a key wrapping a random data key (the
  end-to-end key); the server keeps a verifier. An admin reset deletes the user's data; users change their own and keep it.
  A wrong sign-in costs 1 s, with no lockout (offered one). Advised: one token per device/person, strong passwords.
- **Tokens belong to users** (0.2.2): only their user signs in with one; tokens made before (`UserId` "") work
  for anyone until tied, so no one's locked out by the update. Adding a user (page or `useradd`) makes and copies
  theirs; a user may have several (devices). Revoke and Delete drop connections.
- **Profile fields** (display name, email) aren't sealed: they're for the admin to know who's who. Email is checked.
- **Notepad:** OT on the clients; the server orders sealed changes and keeps snapshots. DA's KnownHost is
  `host:port user`.
- **Files, Processes, Sessions aren't sealed** (the server's files; shared databases): encrypted on the wire only.
- **Sessions bans** stop sharing and editing, not browsing, and remove every matching session; PC and IP bans are
  easy to dodge, user and token bans are firm (Revoke / delete user shuts someone out entirely). Offered: bans that
  block the module entirely.

**Where it stands:**
- TestClient's 78 checks (Files 16, Processes 13, Sessions 20, Users 13) pass on 0.2.2's Debug build. Checked on
  screen via UI Automation (`%TEMP%\dsedit\shot.ps1`, `users.ps1 -Invoke … -Field … -Value …`, `tie.ps1`): Files'
  card, Processes' tab, Sessions' tab (search, Ban PC, Remove, Unban); Users: Edit fills the form, Add gray until a
  change, Update, tying an old token (it moves under the user, the card hides).
- DA through a test server: 3.26.5 Notepad (two copies, live, reconnect), 4.1.0 Processes (`%TEMP%\dsedit\make_devp.ps1
  -Name … -Token … -Port 38991 -User … -Password …`, Support only). `%TEMP%\DaisysDev` is the DA session's: leave it.
- **Not tested:** DA 4.17.0 with Sessions (the brief's steps 1–6); several rows picked at once on the Sessions tab;
  Ban user/address/token and ban by hand; Processes' Export…/Import… and daily fleet broadcast; the installer
  (install, firewall, upgrade, uninstall) and self-update; on the Users page: Add, New token, Copy (they'd overwrite
  the user's clipboard), a new password through Update, Revoke; Settings by hand; the internet. DA doesn't use
  `useredit`, `usertoken` or the profile yet.

**Next (waiting on the user):**
- They update to 0.2.2, then tie their existing tokens to users under Users → Tokens for anyone.
- They update, forward 38900, and try Share Session from DA 4.17.0 (two PCs or users).
- Offered: a sign-in lockout. Later (theirs): sealing the server's stored data.
- The Android session builds from the two briefs (connection and Notepad, then Files).
- Offered: removing DA's unused `RemoteChannels` (Core and the AI module).

**Releasing:** bump the version (README, `src/Directory.Build.props`), `.\build.ps1`, commit, tag `vX.Y.Z`, push,
`gh release create vX.Y.Z artifacts\DaisysServer-Setup-X.Y.Z.exe` (no workflow: built here).
