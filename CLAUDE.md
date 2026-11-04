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

**Asked (2026-10-28, in REQUESTS.md):** a central GUI server for DA clients (token, encrypted TCP, basic/admin
users with data only clients decrypt, modules), Notepad first with DA's side and an Android brief, an installer and
updates; 0.1 to Downloads.

**Asked (2026-10-30):** create the repo as 0.1 and commit DA's side (done: DA 3.26.4). Then, in DA: the server's
settings to Settings → General, no descriptions on that page, Notepad on the server only with no network code of its
own, publish (done: DA 3.26.5). Then a **Files** module: clients (Android first) browse this PC's drives, the shared ones
picked in Settings; everything to list, detail, preview and open; a brief for the Android session; publish (done:
0.1.1, notes in `src/Modules/Files/CLAUDE.md`).

**Asked (2026-11-04):** build the `processes` module from the brief DA's session wrote (in REQUESTS.md): Process
Analyzer's master database (DA 4.1.0), and say what the client needs changed. Built as 0.1.2, not released or
committed (not asked); notes in `src/Modules/Processes/CLAUDE.md`. Changed: `src/Modules/Processes/` (new),
`DaisysServer.sln`, PROTOCOL §6, README (feature, tab, data folder, layout), `src/Directory.Build.props` (0.1.2),
TestClient (`processes:` checks, `report`). Told the user the client needs nothing.

**Done (both released 2026-10-30, `rosscarlson/DaisysServer`, public; the releases are the updater's source):**
- 0.1.0: this whole repo (layout in the README). Downloads: `DaisysServer-Android-Brief.md` (the connection and
  Notepad; updated for DA 3.26.5: no PC hosts).
- 0.1.1: the Files module (notes in `src/Modules/Files/CLAUDE.md`), `IServerModule.SettingsView` (a module's card in
  Settings; the folders card is now **Folders**), `Ui.Size` up to TB, TestClient's `files:` checks (`setup` shares
  %TEMP%'s drive), PROTOCOL §5. Downloads: the 0.1.1 installer and `DaisysServer-Files-Android-Brief.md`.
- DA's side (details in DA's `src/Modules/Tools/CLAUDE.md`): Core 0.50 `src/DaisysApp.Core/Server/` (ServerLink,
  `KeptChannel` via `ServerLink.Keep`, the card on Settings → General, the protocol copies); Tools → Notepad on the
  server only (`NotepadView.ServerNoteLink`). Core's `RemoteChannels` is left there, unused. DA's untracked
  `*-GAMER11.*` files are OneDrive conflict copies, and other sessions' uncommitted work is left alone: commit only
  your own files there.

**Decisions (mine, told to the user):**
- **Connecting:** a token is `dsk1_` + 6 bytes of id + a 32-byte secret. The handshake is ECDH P-256 with HKDF, salted
  with the secret, giving forward secrecy and mutual proof of the token.
- **Signing in:** the password goes through PBKDF2 to make the sign-in proof and a key that wraps a random data key,
  which is the end-to-end key. The server keeps only a verifier.
- **Admin resets:** an admin resetting a password deletes that user's data, since it can't be opened without the old
  one. Users change their own password from a client and keep everything.
- **Notepad:** the operational transformation stays on the clients. The server only orders the sealed changes and
  keeps snapshots plus the changes since. DA translates, so its notepad page is unchanged.
- **KnownHost in DA's Notepad** is `host:port user`, so tabs last in step with another server (or an old PC host) go up
  as new tabs instead of being dropped.
- **Default port:** 38900.
- **Files:** read-only, admins only by default (**Basic users too**), no drive shared until ticked, not sealed (the
  server's own files; encrypted on the connection only), the client pulls pieces of at most 1 MB.
- **Processes** (the brief's): not sealed, one database for every user, in `data\processes\`. Fleet figures go out
  with a version of their own once a day. No editing on the server (optional in the brief).

**Where it stands:**
- **Tested:** TestClient's 32 checks (16 of them Files') pass on 0.1.1's Debug and published builds; Files' Settings
  card on screen. The stored data is ciphertext only. DA 3.26.5: two copies through a test server (tabs up, a poke
  live on both, the server restarted and both back in 5 s), Settings → General on screen.
- **0.1.2 (Processes):** TestClient's 45 checks pass (13 Processes'); two DA 4.1.0 copies synced through a test
  server (full on joining, an edit on both in 4 s, catching up after being stopped); the tab on screen. Not tested:
  Export…/Import…, the daily fleet broadcast.
- DA test copies for Processes: `%TEMP%\dsedit\make_devp.ps1 -Name … -Token … -Port 38991 -User … -Password …`
  (Support only, hidden in the tray); `%TEMP%\DaisysDev` is the DA session's copy: leave it alone.
- **Not tested:** the installer (install, firewall, upgrade, uninstall), the update from 0.1.0 to 0.1.1,
  typing by hand, the Users, Tokens and Settings pages by hand, the internet, admin ops from a client.

**Next (waiting on the user):**
- They try 0.1.1 with DA 3.26.5 (untested: typing by hand, the card's Change password and Forget).
- The Android session builds from the two briefs: the connection and Notepad (the phone's notepad can't reach a PC
  host since DA 3.26.5), then Files. Daisy's App (Windows) has no Files client; not asked.
- 0.1.2: commit and release when they say (asked them). Told them the client's optional nits: `app` is the Support
  DLL's version (4.0.0.0), two DA copies on one PC share a PC row, its README could point at PROTOCOL §6, and a
  database id in `changes` would cover a wiped server growing past a client's version (both sides; not worth it now).
- Offered: removing DA's now-unused `RemoteChannels` (Core, and the AI module's side of it).

**Releasing:** bump the version (README, `src/Directory.Build.props`), `.\build.ps1`, commit, tag `vX.Y.Z`, push,
`gh release create vX.Y.Z artifacts\DaisysServer-Setup-X.Y.Z.exe` (no workflow: built here).
