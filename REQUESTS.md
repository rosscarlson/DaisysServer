# Requests

What was asked, word for word, by date.

## 2026-10-28

New Application
Daisy's Server

Description
Act as a central server that all DA clients can connect to allowing for various modules to send/store/retrieve data to the server and/or other modules in real time. Will need to function over the internet securely, so we need a way to generate a token that can be shared for an encrypted connection, specifying hostname or IP address in the client, pick the port and I assume TCP. We would also need user account, basic and admin, so that various apps such as Notepad isolates it's data from other profiles, with all data stored on the server to be encrypted and only decrypted by the client after being sent over the network. 

The server should be modular in the same way the main app is such that as we build modules for the main app as necessary we can build modules that plug into the server - such that the server would pass any calls that are destinted for specific modules (so that needs to be in the API, so when a remote module calls the server it tells the server what module it is and what module to talk to on the server side) that module activates and runs the appropriate code, that way server and client can both disable pieces of the app they don't want to use.

The first app I'd like to port over to this is Notepad. You'll need to build the server app (I want a GUI app, not a Windows service) with user accounts, the token generation/system, the modular layout and then the Notepad module - including updating that module for the main app and writing a brief I can use to update the Android app to use the same, you'll drop that brief in Downloads when it's ready. Make sure the Notepad service supports various tabs, naming the tabs (and all clients see those names).

When ready build an installer and drop it in Downloads - this will be "Daisy's Server" with an appropriate server icon (still using a daisy somehow). It needs to have auto-update features just like the main app but only with a "Check for udpates" in the settings section, including it checking on launch (with a setting for that in settings).

The main app is in D:\OneDrive\Code\Windows\DaisysApp (see D:\OneDrive\Code\Windows\DaisysApp\README.md for details) - you'll be building this app in THIS working directory (D:\OneDrive\Code\Windows\DaisysServer)

When ready drop that installer in my Downloads so I can test as version 0.1

contineu

## 2026-10-30

Yes, let's setup the new repo, if we make it private we can't use it for updates, is that right? If so then keep it public but if it can be private aweseome. I've made changes to DA Core in other sessions, it should have affected this but you'll want to check. Then yes, commit and publish everything, including changes to DA and give both new subversions (0.1 for DS)

you were working and our session got broken - here is the last thing you said to me:

---
Daisy's Server 0.1 is built, and the installer is in Downloads: `DaisysServer-Setup-0.1.0.exe`. The Android brief is next to it: `DaisysServer-Android-Brief.md`. The Notepad module works end to end in my tests. Updates won't actually work until there's a GitHub repo to update from, which needs your OK (see below).
**What you'll see when you install it**
- A GUI app in the tray. The icon is a dark server tower with a white daisy in front.
- Tabs: **Status**, **Users**, **Tokens**, **Notepad**, **Log** and **Settings**.
- The installer opens Windows Firewall for it. The default port is TCP 38900, and you can change it in Settings.
- Settings has **Check for updates**, plus a check-on-launch option that's on by default. The update flow works like the main app's: a bar offers it, then it downloads, checks the SHA-256 and installs silently.
**To try it**
1. In **Users**, add yourself as an admin. In **Tokens**, make a token and copy it.
2. In Daisy's App, go to Settings → Tools → Notepad → **Daisy's Server**. Enter the address, port, token, user and password, then click **Connect**.
3. Pick **Daisy's Server** in the Notepad's host box. The tabs you already have go up to the server the first time, and tab names show on every client.
**How the security works**
- **Token:** the connection is encrypted with fresh keys every time. Only a client holding the token can make those keys, so a fake server or a man in the middle fails straight away.
- **Your data:** it's sealed on your own clients with a key made from your password. The server stores it but can't read it. I checked the stored files and there was no readable text in them.
- **Password resets:** if an admin resets a user's password *on the server*, that user's data is deleted, because nothing can open it without the old password. A user who changes their own password from Daisy's App keeps everything.
**Modules:** when a client connects, it says which module on the server it wants and which of its own modules is asking. The server passes the call to that module. Either side can switch a module off; if the server's module is off, the client is told so. `PROTOCOL.md` describes the whole protocol.
**Testing**
- **Automated:** 19 end-to-end checks passed on both the debug build and the packaged build. They covered connecting, signing in, live edits, renames, closing tabs, wrong passwords, unknown tokens and password changes.
- **Daisy's App:** I ran two throwaway copies through a test server. Tabs went up, another client's edits and renames showed live, and an edit made while offline merged. I deleted the copies afterwards and didn't touch your running Daisy's App or its settings.
- **Not tested:**
- the installer itself (install, firewall rule, uninstall)
- typing by hand
- connecting over the internet
- updates (no repo yet)
**Decisions for you**
- **Updates need a GitHub repo.** The updater looks for releases at `rosscarlson/DaisysServer`, which doesn't exist yet. Until it does, a check just says you're up to date. Should I create it and publish 0.1.0 there?
- **The Daisy's App changes are uncommitted and unreleased.** The new server code is in DA Core (0.45) and Notepad. A parallel session works in that repo, so I didn't bump the version or tag anything. Your installed Daisy's App won't show the server option until you release it, which I'd make 3.25.1. Should I do that release?
- **Nothing is committed in `DaisysServer` either.** It isn't a git repo yet. Should I set one up?

---

Yes, create the repo (Daisy's Server) as 0.1 - then commit everything that isn't - other sessions have made chnges to DA Core so you'll need to make sure you're good, it shouldn't have touched what you did but check it first then commit everything so I can test everything with the notepad app

Let's move the settings for DS out of the Tools -> Notepad settings page and put it on the main Settings -> General.

Also on that page, Settings/General remove ALL descriptive text - leave the names of the options but all the sentences you added explaining each thing, remove all that, I know what all this is. Also remove the feature in the current Notepad allowing it to connect to anything other than the server - any network code that's in the notepad app should be removed, it should all be in DA Core's API so that all apps can use our connection to the server.

Publish as next subversion when ready

New Module - "Files" - this will allow a remote client, be it Android, etc. to browse the filesystem of the machine that the server is running on. In the settings of the server app we should be able to select the drives that are being shared. It'll need to send those directory listings, file details, etc. - everything the remote app will need to get the details, preview and then open and display the file (we'll handle that client side for common types). Please update the server as necessary and write me a brief I can give to the other session that's creating the Android client. Publish as next subversion when ready

clarify: "**Not encrypted with the user's own key" -** I don't want the files encrypted I just want the connection between the client and server encrypted, is that how it works?

is the server ready for the files client? just answer, no code

give me the details to give to the agent coding that for Android

## 2026-11-04

we're adding a new module that the client has been build for - here is the brief it made for what it needs, build this into Daisy Server and let me know what if anything you need changed/added on the client to make this work:

---

# Brief: Daisy's Server `processes` module (Process Analyzer's master database)

**Repo:** `D:\OneDrive\Code\Windows\DaisysServer` (read its CLAUDE.md, PROTOCOL.md and the Notepad/Files modules first).
**Client (done, Daisy's App 4.1.0):** `D:\OneDrive\Code\Windows\DaisysApp\src\Modules\Support\ProcessAnalyzer\`:
`ServerSync.cs` (the channel), `ProcessDb.cs` (the entry, `KnownProcess`, and the client's merge), README.md
("Daisy's Server" section: the same protocol as below).

## What it's for

Daisy's App's Support → Process Analyzer checks every running program against a database of known programs (threat,
whether it's needed, expected resource use, publisher, folders it belongs in). Each client classifies programs it
doesn't know and users classify them by hand. The server holds the **master database**: every client sends its
changes up and gets everyone else's, and a new client takes the server's database over its own.

## Rules for this module (a deliberate exception)

- **Not sealed.** The entries are programs' names and classifications, not user data, and the server has to read and
  merge them (the user asked for this on 2026-11-04). Store them in plain JSON.
- **One database for the whole server**, shared by all users (not per user): e.g.
  `%LOCALAPPDATA%\DaisysServer\data\processes\processes.json` (plus a per-client table). Not under `users\<id>`.
- A server **version** number (a long) counts changes: every entry changed gets `rev` = the new version.
- Add the module to PROTOCOL.md as section 6 (the `processes` module, "since 0.x"), and to the server's module list
  (switch on/off like the others).

## The channel

The client opens `{"op":"open","module":"processes","from":"ProcessAnalyzer"}`. Messages inside `msg` (JSON, camelCase):

| From | Message | Server does |
|---|---|---|
| client | `{op:"join", version, app}` | Record the client (its client name from the connection, the user, `app`, last seen). Answer `changes` with every entry whose `rev > version`, `full: false`; if `version` is 0, or older than anything you can answer from (you dropped history), or greater than the server's own version (another server's), send **every** entry with `full: true`. Also send `clients` (optional). |
| server | `{op:"changes", version, full, entries:[…]}` | `version` = the server's current version. |
| client | `{op:"seen", version}` | Store that client's version (for the server UI: which PCs are behind). |
| client | `{op:"report", entries:[…]}` (≤200 per message, several may come) | Merge each entry (below). If any changed: bump the version once for the message, set each changed entry's `rev` to it, save, and send `{op:"changes", version, full:false, entries:[the changed ones]}` to **every** open `processes` channel, the sender too (the sender uses it as the acknowledgement). If nothing changed, still answer the sender with `changes` carrying the server's current versions of the entries it sent (so it stops resending). |
| client | `{op:"usage", items:[{name, cpu, peakCpu, memoryMB, peakMemoryMB, samples}]}` (hourly) | Keep the latest per (client, name). For each name, `fleetCpu` = average of clients' `cpu`, `fleetMemoryMB` likewise, `fleetPcs` = number of clients. Put those on the entry; they may ride along with the next `changes` (no version bump needed just for them, or bump daily). |
| server | `{op:"clients", clients:[{name, version, lastSeen}]}` | Optional, after join. |
| server | `{op:"error", message}` | A problem worded for the user. |

## An entry (`KnownProcess`)

```json
{ "name": "chrome", "title": "Google Chrome", "publisher": "Google", "category": "Browser",
  "threat": "Safe", "necessity": "Optional", "resource": "Heavy", "description": "…",
  "paths": ["%ProgramFiles%\\Google"], "source": "user", "reviewed": true,
  "updated": "2026-11-04T22:10:21Z", "by": "GAMER11", "rev": 42,
  "sha256": "…", "vtMalicious": 0, "vtEngines": 72, "vtChecked": "…",
  "fleetCpu": 3.1, "fleetMemoryMB": 820, "fleetPcs": 4 }
```

- `name` is the key: the exe's name, lower case, without `.exe`.
- Enums are strings: `threat` Safe|Low|Medium|High|Dangerous; `necessity` Required|Recommended|Optional|Unnecessary|Unknown;
  `resource` Light|Moderate|Heavy. `publisher` may hold alternatives split by `|`.
- `source`: seed | auto | user | import | server (clients set it; keep what they send, or set "server" on your copy,
  either works). Clients never send their built-in `seed` entries unless changed.
- Keep unknown fields you receive (store the JSON object, not only the known properties), so newer clients' fields
  survive.

## The merge (the same as the client's)

For an incoming entry vs the stored one with the same name:
1. none stored → take it;
2. `reviewed` differs → the reviewed one wins (an automatic classification never replaces a person's);
3. otherwise the newer `updated` wins (ties: keep the stored one).

The `fleet*` fields are the server's own: don't take them from clients.

## Server UI (keep it small: labels, short tooltips)

A **Processes** page: entries (name, title, threat, needed, by, updated, rev), search, the PCs (name, user, version,
last seen, "behind" when their version < the server's), the database version, **Export…** (the same file as the
client's: `{format:"DaisysApp.Processes", formatVersion:1, exported, client, serverVersion, entries}`) and
**Import…** (merge with the rules above, bump the version, push `changes`). Editing an entry on the server is optional
(an edit = a reviewed entry, `by` = the server's name).

## Testing

As the server's CLAUDE.md says: `DAISYSSERVER_HOME=%TEMP%\DaisysServerTest`, `TestClient setup`; a DA copy with the
Support module (`%TEMP%\DaisysDev` has it; its settings in `%APPDATA%\DaisysDev\DaisysServer.json`). On the client,
Settings → Support → Process Analyzer shows the sync status, the database version and the changes waiting to be sent.
Check: a new client gets the whole database (`full`), an edit on one client shows on another within seconds, a
client offline while edits happen catches up on join, a reviewed entry isn't overwritten by an automatic one. Never
stop DaisysApp.exe.

## 2026-11-04

yes, release

## 2026-11-13

Use this brief to update our Server App to support these new sharing features - let's make sure we have an admin tool that would allow for searching/finding sessions so they can be removed one at a time or in bulk, etc. in the event a cheater publishes data (the ability to ban source IP, host, account, etc).

I'll also need to know what port we're running on so I can open the firewall for it

Publish next minor version when ready

Here is the brief, it was attached you just didn't read it - I stopped you as it seemed you couldn't find it

------------------------

# Brief: a `sessions` module for Daisy's Server

Daisy's App 4.17.0 adds **SimEffects → Analysis → Share Session**: racing sessions (telemetry logs) shared on Daisy's
Server, browsed and downloaded by every user of it. The client is done and released. This brief is what the server
needs to add. Its counterpart in the app is `src/Modules/SimEffects/Analysis/Sessions/SessionShares.cs` (`SessionServer`)
in the DaisysApp repo.

## Where it fits

- Add a module `src/Modules/Sessions/`, made like `Processes`: module id **`sessions`**. The client opens it with
  `{op: "open", module: "sessions", from: "Analysis"}` over the usual encrypted connection. **Don't change the
  connection, handshake or `Wire.cs`.** (`Wire.cs` is shared word for word with the app.)
- **Not sealed, and one store for the whole server, not one per user.** It's shared data that every user can read,
  like `processes`. Keep it in `%LOCALAPPDATA%\DaisysServer\data\sessions\`: an index file, `sessions.json`, and one
  file a session, `<id>.gz` (store the bytes exactly as they arrive).
- Add a section to `PROTOCOL.md` (§7, "The `sessions` module (since 0.1.3)") describing what's below. Add a page in
  the server's window too, as Processes has: a list of the sessions (title, driver, track, who shared it, size,
  date) with Delete, and the total size.

## Messages (inside `msg` on the channel)

Every question from the client carries a `rid` (a number). Answer with the same `op` and the same `rid`, or with
`{op: "error", rid, message}`. The message is shown to the user, so write it for people. Answer questions in order.
The client waits 60 s for each answer.

| Ask | Answer |
|---|---|
| `{op: "list"}` | `{op: "list", sessions: [meta]}`: every **complete** session, any order (the client sorts). |
| `{op: "share", meta, size, sha256}` | `{op: "share", id}`: a new upload begins. `meta` is described below (without `id`, `user`, `client`, `shared`, `size` or `mine`). `size` is the byte count of the gzip file; `sha256` is the lowercase hex of its hash. Not listed until it's complete. |
| `{op: "put", id, offset, data, eof}` | `{op: "put", id, received}`: one piece, base64 (the client sends 512 KB pieces, one at a time, in order). `offset` must equal the bytes received so far, or reply with an error. With `eof: true`, check that the total equals `size` and the SHA-256 matches. If so, the session is complete: write it and add it to the index, then **push** `added` (below). If not, reply with an error and drop it. |
| `{op: "get", id, offset, length}` | `{op: "get", id, offset, length, size, eof, data}`: up to `length` bytes (the client asks for 1 MB; cap it at 1 MB) from `offset`, as base64, where `length` is how many came. `eof` is true when this piece reaches the end. |
| `{op: "edit", id, title, description, video, videoAt}` | `{op: "edit", session: meta}`: the sharer changes the title and description, and the YouTube link and where the session starts in it (seconds; either can be null). **Push** `changed`. |
| `{op: "delete", id}` | `{op: "delete", id}`: delete the file and the index entry. **Push** `removed`. |

**Pushes** have no `rid`. Send each one to every open `sessions` channel, the sender's included:
`{op: "added", session: meta}`, `{op: "changed", session: meta}`, `{op: "removed", id}`.

An op the module doesn't know gets `{op: "error", rid, message: "This server can't do \"…\" yet: update it."}`.

## `meta`

```
{ id, title, description, driver, game, track, car, class, laps, best, started, video, videoAt,   // from the client
  user, client, shared, size, mine }                                                               // set by the server
```

- From the client (store them as they come, and keep fields you don't know): `title` and `description` (text, the
  description multi-line), `driver` (the driver's name), `game` (`FH5`, `iRacing`, `ACC`…), `track`, `car`, `class`
  (may be ""), `laps` (a number), `best` (the best lap in seconds, 0 for none), `started` (ms since 1970, UTC),
  `video` (a YouTube URL or null), `videoAt` (seconds or null).
- Set by the server: `id` (make it: 16 random hex characters), `user` (the signed-in user's name), `client` (the
  client's PC name, from the channel), `shared` (ms since 1970, UTC, when it was completed), `size` (bytes of the
  `.gz`).
- `mine`: work it out **per recipient**. It's true when the session's sharer is the user this channel belongs to. So
  build `list` answers and pushes for each channel, not once for all.

## Rules

- **Who can do what:** any signed-in user can `list`, `get` and `share`. `edit` and `delete` are allowed for the
  user who shared it, and for admins. Anyone else gets "Only the person who shared it can change it."
- **Limits:** `size` up to 200 MB. Refuse more ("That session is too big to share (over 200 MB)"). A `put` must keep
  each frame well under `Wire.MaxFrame`. Drop uploads that never reach `eof` after 10 minutes, or when their channel
  closes.
- **Data:** the `.gz` is a gzip of Daisy's App's session export (UTF-8 CSV, `# ForzaWheel session,2` header). The
  server needn't open it. Don't trust anything inside it.
- Saving: write the index a second after a change (as Processes does), and write files whole or not at all (a temp
  file, then rename).
- Logging: one line per share, edit, delete and download (who, id, size), in the server's log.

## Security (as it stands)

- Everything goes over Daisy's Server's existing connection. It's encrypted with **AES-256-GCM**, using keys from an
  ephemeral **ECDH P-256** exchange, salted (HKDF-SHA256) with the token's 32-byte secret. The secret is never sent,
  so only someone who has the token can talk to the server, and a man in the middle gets nothing.
- Shared sessions are **not** sealed with a user's data key, because other users must read them. They're readable on
  the server's disk (the same as `processes`). Say so in PROTOCOL.md.

## To test

Daisy's App 4.17.0: Settings → General → Daisy's Server set up. Then go to SimEffects → Analysis and click the share
icon at the top right of the Analysis card.

1. Share a session with an edited title. It should appear in the list ("On Daisy's Server").
2. Change the title and press Share again. Two clients should both see the change, through the push.
3. Remove it.
4. As a second user, Compare a session. It downloads into Session 2.
5. Delete someone else's as a non-admin: refused.
6. Tick "Share each session automatically" and race. Each session should be shared after its YouTube upload.

📎 BRIEF-sessions-module.md
