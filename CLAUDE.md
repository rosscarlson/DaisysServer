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

**Asked (2026-10-30):** create the repo as 0.1, then commit DA's side, after checking other sessions' Core changes.

**Done:** this whole repo (layout in the README), 0.1.0. In Downloads: `DaisysServer-Setup-0.1.0.exe`,
`DaisysServer-Android-Brief.md` (its DA version updated to 3.26.4). Released 2026-10-30: the repo is
`rosscarlson/DaisysServer` (public), tag `v0.1.0` with the installer (the updater's source). Other sessions hadn't
touched Notepad or `Server/`; only Core's version number clashed (they took 0.46–0.48), so ours is 0.49. DA's side went
out in DA 3.26.4 (also in 3.26.3, which another session re-tagged on top of it). DA's untracked `*-GAMER11.*` files are
OneDrive conflict copies, left out. DA's side:
- Core 0.49 `src/DaisysApp.Core/Server/` (ServerLink, ServerSettingsView, the protocol copies);
- Tools → Notepad's `NotepadView.ServerNoteLink`;
- docs: the Notepad README, API.md, `src/Modules/Tools/CLAUDE.md`, `docs/UNTESTED.md`.

**Decisions (mine, told to the user):**
- **Connecting:** a token is `dsk1_` + 6 bytes of id + a 32-byte secret. The handshake is ECDH P-256 with HKDF, salted
  with the secret, giving forward secrecy and mutual proof of the token.
- **Signing in:** the password goes through PBKDF2 to make the sign-in proof and a key that wraps a random data key,
  which is the end-to-end key. The server keeps only a verifier.
- **Admin resets:** an admin resetting a password deletes that user's data, since it can't be opened without the old
  one. Users change their own password from a client and keep everything.
- **Notepad:** the operational transformation stays on the clients. The server only orders the sealed changes and
  keeps snapshots plus the changes since. DA translates, so its notepad page is unchanged.
- **Where the DA card lives:** on Notepad's Settings page, because the shell was being changed by another session.
  It could move to Settings → General later.
- **Default port:** 38900.

**Where it stands:**
- **Tested:** TestClient's 19 checks pass on the Debug and the published builds. Two DA copies worked through a test
  server. The stored data is ciphertext only.
- **Not tested:** the installer (install, firewall, upgrade, uninstall), an update from a release (needs 0.1.1),
  typing by hand, the Users, Tokens and Settings pages by hand, the internet, admin ops from a client.

**Next (waiting on the user):**
- They try 0.1 with DA 3.26.4's Notepad.
- Android, per the brief.

**Releasing:** bump the version (README, `src/Directory.Build.props`), `.\build.ps1`, commit, tag `vX.Y.Z`, push,
`gh release create vX.Y.Z artifacts\DaisysServer-Setup-X.Y.Z.exe` (no workflow: built here).
