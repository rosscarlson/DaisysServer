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

**Asked (2026-10-28):** the request is in REQUESTS.md. In short:
- a central server for DA clients, as a GUI app (not a service);
- a shared token and an encrypted TCP connection, with the host and port set in the client;
- basic and admin users, each user's data isolated and encrypted so only clients decrypt it;
- modular like DA, with calls routed to modules;
- Notepad first, with named tabs, plus DA's Notepad updated and an Android brief in Downloads;
- an installer with a daisy server icon, and updates through Settings → Check for updates plus a check-on-launch
  setting;
- version 0.1, delivered to Downloads.

**Asked (2026-10-30):** create the repo as 0.1 and commit DA's side (done: DA 3.26.4). Then, in DA: the server's
settings to Settings → General, no descriptions on that page, Notepad on the server only with no network code of its
own, publish (done: DA 3.26.5).

**Done:** this whole repo (layout in the README), 0.1.0, released 2026-10-30: `rosscarlson/DaisysServer` (public), tag
`v0.1.0` with the installer (the updater's source). In Downloads: the installer and `DaisysServer-Android-Brief.md`
(updated for 3.26.5: no PC hosts). DA's side (details in DA's `src/Modules/Tools/CLAUDE.md`):
- Core 0.50 `src/DaisysApp.Core/Server/`: ServerLink, `KeptChannel` (`ServerLink.Keep`: a module's channel kept open,
  reconnecting), ServerSettingsView (the card, on Settings → General), the protocol copies;
- Tools → Notepad: `NotepadView.ServerNoteLink` only (no host box, no PC hosting; `NoteServer.cs` deleted).
- Core's `RemoteChannels` (the AI port's module channels) is left in DA, now unused.
- DA's untracked `*-GAMER11.*` files are OneDrive conflict copies; another session's uncommitted AI work there is left
  alone (commit only your own files there).

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

**Where it stands:**
- **Tested:** TestClient's 19 checks pass on the Debug and the published builds. Two DA copies worked through a test
  server. The stored data is ciphertext only. DA 3.26.5: two copies through a test server (tabs up, a poke live on
  both, the server restarted and both back in 5 s), Settings → General on screen.
- **Not tested:** the installer (install, firewall, upgrade, uninstall), an update from a release (needs 0.1.1),
  typing by hand, the Users, Tokens and Settings pages by hand, the internet, admin ops from a client.

**Next (waiting on the user):**
- They try 0.1 with DA 3.26.5 (untested: typing by hand, the card's Change password and Forget).
- Android, per the brief (the phone's notepad can't reach a PC host since DA 3.26.5).
- Offered: removing DA's now-unused `RemoteChannels` (Core, and the AI module's side of it).

**Releasing:** bump the version (README, `src/Directory.Build.props`), `.\build.ps1`, commit, tag `vX.Y.Z`, push,
`gh release create vX.Y.Z artifacts\DaisysServer-Setup-X.Y.Z.exe` (no workflow: built here).
