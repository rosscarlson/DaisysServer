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
- 2026-10-28 to 11-04: 0.1.0 (server, Notepad; DA 3.26.4/5's Settings → General card), **Files** 0.1.1, **Processes**
  0.1.2 (each module's CLAUDE.md); Android briefs in Downloads (`DaisysServer-Android-Brief.md`, `…-Files-…`).
- 2026-11-13: **Sessions** 0.2.0 (DA 4.17.0's Share Session, `docs/BRIEF-sessions-module.md`; admin tab: find,
  remove, ban), port **38900** TCP (forward it).
- 2026-11-14: 0.2.1 (profile fields from `UserStore.Fields`, `useredit`, icon buttons, version in title), 0.2.2 (one
  Add/Edit form, password twice), 0.2.3 (Groups Admin/User/SE-Analysis as `Core/Modules/MultiPick.cs`, no help text).
  0.2.4 (another session): Sessions sends `displayName` (looked up by id) for DA 4.17.21's Driver column.
- 2026-11-17: **0.3.0**, the user's asks: **tokens fully removed** (they're redundant with passwords once clients pin
  the server: told them the costs: anyone can reach sign-in, no per-device revoke, the key must be kept); **the
  server's key** (`ServerSettings.Key`, P-256 PKCS#8; fingerprint on Status with Copy; handshake v2 = ee‖es, PROTOCOL
  §1); **signing in optional** (anonymous: `IServerModule.Anonymous`, only Sessions; `ServerUser.Anonymous`,
  `anon:<hash of hello's device>`); **5 wrong passwords per IP in 15 min lock it 15 min** (`SignInLimit`); IP kept as
  before (Sessions bans by it). Close-to-tray was already there (`KeepInTray`, on): checked it works. Removed:
  `TokenStore` (`tokens.json` deleted at start), the Token row/card, `usertoken`, token bans/columns,
  `IModuleChannel.TokenId/TokenName`, `UserStore.Deleted`. DA's side is a brief: `docs/BRIEF-0.3.0-client.md` (also in
  Downloads): copy Wire.cs/ServerClient.cs, built-in host+key, device id, TOFU, optional sign-in. Not done in DA: old
  DA can't connect to 0.3.0 until it is.
- DA's side lives in DA (`src/DaisysApp.Core/Server/`, details in DA's `src/Modules/Tools/CLAUDE.md`). There, DA's
  `*-GAMER11.*`/`*-PLEX*` files are OneDrive conflict copies and other sessions' work is left alone: commit only
  your own files. DA's local clone may be behind GitHub (other PCs release): `git fetch`/`git show origin/main:…`.

**Decisions (mine, told to the user):**
- **Connecting (0.3.0):** the client pins the server's key fingerprint (built in, or trust on first use); ECDH
  ephemeral + with the server's key, HKDF (forward secrecy; only that server makes the keys). **Signing in:** PBKDF2
  makes the sign-in proof and a key wrapping a random data key (the end-to-end key); the server keeps a verifier. An
  admin reset deletes the user's data; users change their own and keep it. A wrong sign-in costs 1 s, plus the IP limit.
- **Anonymous** is per module (only Sessions); a share is its install's (`device`), shown by PC name. PC, IP and
  anonymous-install bans are easy to dodge; user bans and deleting the user are firm.
- **No help text on the pages** (the user's call, 0.2.3): labels and tooltips only.
- **Profile fields** (display name, email) aren't sealed: they're for the admin to know who's who. Email is checked.
- **Notepad:** OT on the clients; the server orders sealed changes and keeps snapshots. DA's KnownHost is
  `host:port user`.
- **Files, Processes, Sessions aren't sealed** (the server's files; shared databases): encrypted on the wire only.

**Where it stands:**
- TestClient's 89 checks pass on 0.3.0's Debug build (key pinned/mismatch, anonymous refused Notepad/users, anonymous
  share/mine/edit/delete by install and refused to another, usertoken gone, the IP lockout last: restart the test
  server to rerun). On screen (`%TEMP%\dsedit\shot.ps1`): Status' key row, Users without tokens, Sessions with an
  *(anonymous)* row, ✕ to the tray.
- **Not tested:** DA against 0.3.0 (DA's brief); the installer and self-update; Processes' Export…/Import…; several
  rows picked at once on Sessions; Users' Add/new password through Update; the internet. Groups do nothing yet
  beyond Admin.

**Next (waiting on the user):**
- They update the server to 0.3.0, copy its key from Status, and give it (and their public address) to the DA agent
  with the brief; until DA ships it, DA can't connect.
- Offered: sealing the server's stored data (later, theirs). The Android session builds from the briefs (now v2).
- Offered: removing DA's unused `RemoteChannels` (Core and the AI module).

**Releasing:** bump the version (README, `src/Directory.Build.props`), `.\build.ps1`, commit, tag `vX.Y.Z`, push,
`gh release create vX.Y.Z artifacts\DaisysServer-Setup-X.Y.Z.exe` (no workflow: built here).
