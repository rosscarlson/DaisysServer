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
