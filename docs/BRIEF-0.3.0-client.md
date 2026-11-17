# Brief: Daisy's App for Daisy's Server 0.3.0 (server key, no tokens, anonymous Share Session)

For the agent working in Daisy's App (`D:\OneDrive\Code\Windows\DaisysApp`). Daisy's Server 0.3.0 is released. Its
`PROTOCOL.md` §1–3 and §7 describe the wire; this brief describes what changes in the app.

## What changed on the server

- **Tokens are gone.** There's no `dsk1_` token. Clients don't send one, the server doesn't make them, and op
  `usertoken` and `useradd`'s `token` answer were removed.
- **The server has a key** (P-256), and clients know it by its **fingerprint**: 43 characters of base64url, shown on
  the server's Status page with a Copy button. The handshake (protocol **v2**) mixes in an ECDH with that key, so only
  the real server can complete a connection. The client checks the fingerprint before sending anything.
- **Signing in is optional.** A connection that doesn't sign in is anonymous. It can open channels only to modules
  that allow it: today that's just `sessions` (`welcome.modules[].anonymous`). Other modules answer `open` with
  *Sign in to use Notepad.*, and other ops get *Sign in first.*
- **The hello carries `device`:** a random id each install makes once, keeps and never shows. An anonymous client's
  shared sessions belong to it by this id: `mine` is true for it, and only it can edit or delete them.
- **Anonymous sessions:** `user` is `""`, `anonymous` is `true`, and `displayName` is the sharer's PC name.
- **Bans:** by user (for an anonymous sharer, their install), PC, or IP address. There are no token bans.
- **Wrong passwords:** 5 from one IP address within 15 minutes lock that address out of signing in for 15 minutes,
  even with the right password. The error says *Too many wrong passwords from your address: try again in N minutes.*
  Connecting anonymously still works.
- **Version mismatches:** an old (v1) client connecting to 0.3.0 gets *The server is newer than this client: update
  it.* The new client against an old server says *The server is older than this client: update it (to 0.3.0 or
  later).*

## 1. Copy the two shared files (word for word)

From `D:\OneDrive\Code\Windows\DaisysServer\src\DaisysServer.Core\Protocol\` to DA's `src\DaisysApp.Core\Server\`:

- `Wire.cs`:
  - `Token` is gone.
  - `ServerKey.Fingerprint(spki)` is new.
  - `Wire.ConnectAsync(stream, serverKey, ct)` now takes the server key; `Wire.ServerKey` is new.
- `ServerClient.cs`, the new signature:
  ```csharp
  ServerClient.ConnectAsync(string host, int port, string? serverKey, string? user, string? password,
      string clientName, string app, string device, CancellationToken ct)
  ```
  - `serverKey`: the fingerprint expected. Pass `""` the first time; the client then accepts any key and you keep
    `client.ServerKey`.
  - `user` empty or null connects anonymously.
  - New members: `client.ServerKey` (the fingerprint it saw) and `client.Anonymous`. `DataKey` is empty when
    anonymous.
  - A key that doesn't match throws a `ServerException`: *…doesn't have the key of the server you know there: it may
    not be yours. If the server was reinstalled, forget its key and connect again.*

The server repo's TestClient links these same files, so they build on net8.0 with no WPF.

## 2. `ServerLinkSettings` (`src/DaisysApp.Core/Server/ServerLink.cs`)

- **Remove** `TokenProtected` and `Token`. Old `DaisysServer.json` files keep a `TokenProtected` value; it's ignored
  on load and dropped on the next save.
- **Add** `ServerKey`, the fingerprint. It's plain text, because it isn't a secret.
- **Add** `Device`: 32 random hex characters, made the first time it's empty and saved. Don't show it, and don't log
  it. It's what makes anonymous shares "mine".
- **Built-in server.** Add constants for Ross's server: `BuiltInHost`, `BuiltInPort` (38900) and `BuiltInKey`.
  - Ask Ross for the address.
  - The key appears on his server's Status page after it updates to 0.3.0; ask him to paste it.
  - When `Host` is empty, use these, so Share Session works with nothing set up.
- **Rename the state.**
  - `Complete` becomes "has a server": `Host.Length > 0`, or the built-in one.
  - Add `SignedIn`: `User.Length > 0 && PasswordProtected.Length > 0`.

## 3. `ServerLink`

- `ClientAsync`:
  1. Pick the host, port and key:
     - The built-in host: use `BuiltInKey`.
     - Any other host: use `Settings.ServerKey`, which is `""` until the first connection.
  2. Call
     `ServerClient.ConnectAsync(host, port, key, s.SignedIn ? s.User : null, s.SignedIn ? s.Password : null, Environment.MachineName, AppPaths.ShortName, s.Device, ct)`.
  3. If `Settings.ServerKey` was empty, save `c.ServerKey` (trust on first use).
  4. Log `as {c.User}`, or `anonymously`.
- `Configured` means there's a server, which is always true with the built-in one. Add `SignedIn` for the modules
  that need a user.
- `SetUpAsync(host, port, user, password)`:
  - Drop the token parameter. User and password are optional, but give both or neither.
  - When the host or port changes, clear `ServerKey` so the new server is trusted on first connect, unless it's the
    built-in one.
- Add `ForgetKey()`: clears `ServerKey`, disconnects, and saves. It's for after the server is reinstalled.

## 4. Settings → General → Daisy's Server (`ServerSettingsView.cs`)

- Remove the **Token** row and its `lang` string (`"Token"` in `src/DaisysApp/lang/en.json` and the other languages).
- **Address** and **Port** stay. When they're empty, show the built-in server as a placeholder.
- **User** and **Password** are optional. With both empty, Connect connects anonymously.
- Add a **Server key** row: the fingerprint, read-only, with **Forget** (two-click `Ui.Confirm`). It's empty until the
  first connection.
- Status text: *Connected to {0} as {1}*, or *Connected to {0} (not signed in: Share Session only)*.
- On a key mismatch, show the server's message as the problem.

## 5. Modules

- **Notepad** (`Tools/Notepad`), **Process Analyzer's** `ServerSync`, and anything else that opens a module other
  than `sessions` need a user.
  - Check `ServerLink.SignedIn` where they now check `Configured`.
  - When nobody's signed in, keep their old behaviour: local, or the "set up the server" hint with *sign in* wording.
  - Their `Identity` strings are unchanged.
- **Share Session** (`SimEffects/Analysis/Sessions/SessionShares.cs`) works signed in or not.
  - `Mine` already comes from the server, and is right for anonymous shares from this install.
  - `Name => DisplayName…` already shows the PC name for anonymous ones. Optionally read `anonymous` and mark those
    rows (*Anonymous*).
  - Line ~337 sets `driver = ServerLink.Settings.User` when signed in. Anonymous, leave it as it is: the server names
    them by their PC.
  - The auto-share check (`SessionShares.All.Auto && ServerLink.Configured`) becomes true for everyone once the
    built-in server exists. Keep auto-share opt-in (off by default), so nothing is shared without the user turning it
    on.
- Errors a user may now see, already worded, so show them as they come:
  - the key mismatch,
  - *The server is older than this client…*,
  - *Too many wrong passwords from your address…*,
  - *Sign in to use {module}.*

## 6. Test (the server's rules: never the real server's files)

1. In the server repo, `dotnet build DaisysServer.sln`.
2. `TestClient setup %TEMP%\DaisysServerTest tester Test-pass-123` prints the test server's key. It's on port
   38991.
3. Start `src\DaisysServer\bin\Debug\net8.0-windows\DaisysServer.exe --tray` with
   `DAISYSSERVER_HOME=%TEMP%\DaisysServerTest`.
4. In a throwaway DA copy, point the server at `127.0.0.1:38991` and check:
   - (a) No user: Connect works, the key gets kept, Share Session lists, shares, edits and deletes your own share, and
     the server's Sessions tab shows *(anonymous)* with the PC. Notepad asks you to sign in.
   - (b) With `tester` / `Test-pass-123`: Notepad works, and shares show as `tester`.
   - (c) Change the stored key by one character: the mismatch message appears and nothing is sent. **Forget** fixes
     it.
   - (d) A second throwaway copy, anonymous: it sees the first copy's share, not as its own, and can't delete it.
5. Afterwards: `DaisysServer.exe --exit` with the same `DAISYSSERVER_HOME`, then delete `%TEMP%\DaisysServerTest` and
   the throwaway copies along with their `%APPDATA%` and `%LOCALAPPDATA%` folders. Never stop `DaisysApp.exe`.

## 7. Daisy's App Mobile (Android)

The same protocol changes apply; see PROTOCOL.md §1 for the handshake and §2 for optional sign-in. For Kotlin:

- Read `key` from the server's hello and check its fingerprint, `base64url(SHA-256(SPKI))` with no padding.
- Compute `es` = ECDH(the client's ephemeral key, the server's key) as well as `ee`.
- `ikm` = `ee ‖ es`; `salt` = SHA-256(server `pub` ‖ client `pub` ‖ `key`); `info` = `"DaisysServer v2" ‖ nonces`.
- Send `v: 2` and `device` in the hello.
