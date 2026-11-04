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
