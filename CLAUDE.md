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
- 2026-11-14: **0.2.2** (released): one form on Users for Add and Edit (Update, or Confirm? with a new password; Add
  gray until changed; ✕), the password twice, **tokens merged into Users** (`TokensPage` gone; `TokenStore.UserId`,
  `Assign`, `RevokeFor`; login refuses another user's token; `useradd` answers `token`; op `usertoken`).
- 2026-11-14: **0.2.3** (released): password boxes 2/3 wide without "Again", every other box as wide as both;
  **Groups** (Admin, User, SE-Analysis: `UserStore.Groups`, a line to add one; `User.Groups`; Admin = `User.Admin`;
  old users got Admin+User or User) as a checkbox dropdown (`Core/Modules/MultiPick.cs`), new users in User (the first
  in Admin too); editing, a **Token** row under Email (hidden; Copy, Replace = Confirm?, or New token); user rows just
  Edit/Delete; all help text removed (the user's wish: they know it). Wire: `groups` in `users`, `useradd`, `useredit`.
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
- **No help text on the pages** (the user's call, 0.2.3): labels and tooltips only.
- **Profile fields** (display name, email) aren't sealed: they're for the admin to know who's who. Email is checked.
- **Notepad:** OT on the clients; the server orders sealed changes and keeps snapshots. DA's KnownHost is
  `host:port user`.
- **Files, Processes, Sessions aren't sealed** (the server's files; shared databases): encrypted on the wire only.
- **Sessions bans** stop sharing and editing, not browsing, and remove every matching session; PC and IP bans are
  easy to dodge, user and token bans are firm (Revoke / delete user shuts someone out entirely). Offered: bans that
  block the module entirely.

**Where it stands:**
- TestClient's 80 checks (Files 16, Processes 13, Sessions 20, Users 15) pass on 0.2.3's Debug build. Checked on
  screen via UI Automation (`%TEMP%\dsedit\shot.ps1`, `users.ps1 -Invoke … -Field … -Value …`, `tie.ps1`): Files'
  card, Processes' tab, Sessions' tab (search, Ban PC, Remove, Unban); Users (0.2.3): the form's widths, Groups dropdown
  (tick, Update saves), Token row, tying an old token.
- DA through a test server: 3.26.5 Notepad (two copies, live, reconnect), 4.1.0 Processes (`%TEMP%\dsedit\make_devp.ps1
  -Name … -Token … -Port 38991 -User … -Password …`, Support only). `%TEMP%\DaisysDev` is the DA session's: leave it.
- **Not tested:** DA 4.17.0 with Sessions (the brief's steps 1–6); several rows picked at once on the Sessions tab;
  Ban user/address/token and ban by hand; Processes' Export…/Import… and daily fleet broadcast; the installer
  (install, firewall, upgrade, uninstall) and self-update; on the Users page: Add, Copy, Replace/New token (they'd
  overwrite the user's clipboard), a new password through Update; Settings by hand; the internet. DA doesn't use
  `useredit`, `usertoken`, the profile or groups yet. Groups do nothing yet beyond Admin.

**Next (waiting on the user):**
- They update to 0.2.3, then tie their existing tokens to users under Users → Tokens for anyone.
- They update, forward 38900, and try Share Session from DA 4.17.0 (two PCs or users).
- Offered: a sign-in lockout. Later (theirs): sealing the server's stored data.
- The Android session builds from the two briefs (connection and Notepad, then Files).
- Offered: removing DA's unused `RemoteChannels` (Core and the AI module).

**Releasing:** bump the version (README, `src/Directory.Build.props`), `.\build.ps1`, commit, tag `vX.Y.Z`, push,
`gh release create vX.Y.Z artifacts\DaisysServer-Setup-X.Y.Z.exe` (no workflow: built here).
