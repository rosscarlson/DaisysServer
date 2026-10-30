# Daisy's Server's protocol

How a client (Daisy's App, Daisy's App Mobile, `tools/TestClient`) talks to Daisy's Server. **This file is the one
description of it.** The code is `src/DaisysServer.Core/Protocol/` (`Wire.cs`, `ServerClient.cs`), copied word for word
into Daisy's App's `src/DaisysApp.Core/Server/`. A change to either copy goes into both.

Version 1 (Daisy's Server 0.1.0, Daisy's App 3.25.1).

## 1. Connecting

TCP to the server's port (38900 unless it's changed). Every frame, both ways, is a **4-byte little-endian length**
followed by that many bytes.

You need three things to connect: the server's address, its port, and a **token**. A token looks like
`dsk1_<base64url, no padding>`. The 38 bytes inside are:

- 6 bytes: the token's id. This is sent in the clear, as lowercase hex, so the server knows which token you're using.
- 32 bytes: the token's secret. This is never sent.

### Handshake (plain JSON frames)

1. The server sends `{"app": "DaisysServer", "v": 1, "nonce": <base64, 32 bytes>, "pub": <base64>}`.
   - `pub` is an ephemeral P-256 public key, SubjectPublicKeyInfo DER. That's Java's `PublicKey.getEncoded()` and
     .NET's `ExportSubjectPublicKeyInfo`.
   - If `app` isn't `DaisysServer`, it isn't a Daisy's Server. If `v` is higher than you know, the server is too new.
2. The client sends `{"token": "<id, hex>", "nonce": <base64, 32 bytes>, "pub": <its ephemeral P-256 SPKI, base64>}`.
3. Both sides work out the keys:
   - `shared` = ECDH(P-256): the raw x coordinate, 32 bytes. That's what Java's `KeyAgreement("ECDH")` returns.
   - `keys` = HKDF-SHA256(ikm = `shared`, salt = the token's 32-byte secret,
     info = ASCII `"DaisysServer v1"` ‖ server nonce ‖ client nonce, length 64).
   - Bytes 0–31 are the client→server key. Bytes 32–63 are the server→client key.

### Encrypted frames

After the handshake, every frame is one JSON message (UTF-8), sealed with **AES-256-GCM** using that direction's key:

- The frame is the ciphertext, then the 16-byte tag. That's what Java's `AES/GCM/NoPadding` writes.
- The nonce isn't sent. It's 4 zero bytes, then a little-endian `uint64` count of the frames sent that way, starting
  from 0.
- There's no associated data.

Each side counts the other's frames. A frame that doesn't open (the wrong token, tampering, a replay or a reorder) ends
the connection.

The ephemeral keys give forward secrecy. Without the token's secret, nobody can make the keys, so a man in the middle
fails on the first frame, and so does a fake server.

### Hello

| Client | Server |
|---|---|
| `{"op": "hello", "client": "<device name>", "app": "<app name>", "v": 1}` | `{"op": "welcome", "server": "<its name>", "version": "0.1.0", "v": 1, "modules": [{id, name, version}]}` |

`modules` lists the modules switched on there.

If the token is unknown or revoked, or the secret is wrong, the server says nothing. It waits about 1.5 s and closes the
connection, so guessing is slow. A client that's closed on before `welcome` should tell the user *the server didn't
accept the token*.

## 2. Signing in

Users are made on the server, by its window or by an admin client. A user's password never leaves the client.

1. `{"op": "prelogin", "user"}` → `{"op": "prelogin", "salt": <base64, 16 bytes>, "iter": 210000}`.
   - A name with no user still gets a salt. It's made up, but it's the same every time, so a client can't tell which
     names exist.
2. The client works out three keys:
   - `master` = PBKDF2-HMAC-SHA256(password as UTF-8, salt, iter, 32 bytes).
   - `auth` = HMAC-SHA256(key = `master`, message = ASCII `"DaisysServer auth"`).
   - `wrap` = HMAC-SHA256(key = `master`, message = ASCII `"DaisysServer wrap"`).
3. `{"op": "login", "user", "auth": <base64>}` → `{"op": "login", "user", "admin": bool, "vault": <sealed> | null}`.
   - A wrong name or password gets `{"op": "error", "message": "Wrong user name or password."}` after a second's pause.
   - After 5 wrong tries, the connection is closed.
   - The server keeps only SHA-256(its salt ‖ `auth`), never `auth` itself.
4. `vault` is the user's **data key**: 32 random bytes, sealed with `wrap` (see *Sealing* below).
   - If it's null, this is the user's first sign-in. Make 32 random bytes, seal them with `wrap`, and send
     `{"op": "vault", "vault"}`.
   - The answer is `{"op": "vault", "vault"}`: the one the server kept. The first one sent wins, so use the answer.
   - If it doesn't open with `wrap`, the password is wrong for this data. That happens after an admin has reset it.

**Sealing** is AES-256-GCM with a random 12-byte nonce. It's written as base64 of nonce ‖ ciphertext ‖ tag. Everything a
module stores is sealed on the client with the data key, so the server keeps it but can't read it.

**Changing the password** (signed in): `{"op": "password", "auth": <old auth>, "salt": <new 16 bytes, base64>, "iter",
"newAuth": <auth from the new password and salt>, "vault": <the same data key sealed with the new wrap>}` →
`{"op": "password"}`. Nothing else changes.

If an admin sets a new password on the server, the user's data is **deleted**, since it can't be opened without the old
one. Their connections are closed with `bye`.

## 3. After signing in

Every question can carry a `"rid"` (a number). Its answer, or its `error`, carries the same `rid`.

| Ask | Answer |
|---|---|
| `{"op": "ping"}` | `{"op": "pong"}`. Send one every 30 s: a connection that's silent for 2 minutes is dropped. |
| `{"op": "open", "ch": n, "module": "<server module id>", "from": "<client module id>"}` | `{"op": "opened", "ch"}`, or an `error`: the server doesn't have that module, or it's switched off there. |
| `{"op": "close", "ch"}` | Ends a channel. Not answered. |
| `{"op": "password", …}` | See above. |
| **Admins only:** `{"op": "users"}` | `{"op": "users", "users": [{id, name, admin, created, lastSignIn}]}` |
| `{"op": "useradd", "name", "password", "admin"}` | `{"op": "useradd"}`. The password goes over the encrypted connection, and the server makes the verifier from it. |
| `{"op": "userdelete", "id"}`, `{"op": "useradmin", "id", "admin"}`, `{"op": "userpassword", "id", "password"}` | Their op echoed back, or an `error`. `userpassword` deletes the user's data (see above). |

The server can send these at any time:

- `{"op": "close", "ch", "message"}`: the module ended the channel.
- `{"op": "bye", "message"}`: the connection is ending. The token was revoked, the account was changed, the server is
  stopping, or there were too many tries.

An op the server doesn't know gets an `error` saying *This server can't do "…" yet: update it.*

### Channels: talking to a server module

A channel is a conversation between one of the client's modules and the server module it names. A client has one
connection with any number of channels on it. The client numbers its channels.

A message on a channel goes both ways as `{"ch": n, "msg": {…}}`, with no `op` at the top level. What goes inside
`msg` is up to the module (below).

The server hands each channel to the module named in `open`, along with:

- the signed-in user,
- the client's name,
- `from`.

Each user's data is kept apart, in `%LOCALAPPDATA%\DaisysServer\data\users\<user id>\<module>\`. A module switched off
on the server isn't loaded at all.

## 4. The `notepad` module

Daisy's App's Tools → Notepad notes, per user, in tabs with names that every client sees.

The messages are the same as Daisy's App's own `notepad` channel (its API.md, *notepad*), with two differences:

- Every name, note and change is **sealed** with the user's data key. The server never reads them.
- The server keeps each note as a **snapshot plus the changes since**, not as whole text.

This is how a Daisy's App host works: the server numbers the changes, and turns back one made on an old version. The
clients merge the changes themselves, using operational transformation (`NoteText`).

| From | Message |
|---|---|
| a client | `{op: "join", cid}`: `cid` is the notepad's own id. Then: |
| | `{op: "edit", note, base, seq, parts: seal(JSON of the change's parts)}` |
| | `{op: "new", id, name: seal(name) or null, doc: seal(JSON of the runs)}`: the id is made by the client. |
| | `{op: "close", id}` |
| | `{op: "rename", id, name: seal(name)}` |
| | `{op: "snap", note, v, doc: seal(runs at version v)}`: the server can drop the changes up to `v`. |
| | `{op: "resync"}`: asks for `state` again. |
| the server | `{op: "state", you, seq, notes: [{id, name, v, sv, snap, ops: [{v, parts}]}]}` on joining, and after `resync`. |
| | `{op: "edit", note, v, id, by, parts}` to every client, the sender too: that's its answer. `id` is the sender's `you`, and `by` is its client name. |
| | `{op: "new", id, name, v, doc}`, `{op: "close", id}`, `{op: "rename", id, name}` to every client. |
| | `{op: "stale"}`: your edit wasn't made on the latest version. Redo it on what you've received since. |
| | `{op: "who", pcs: [client names]}` |

- **A note**: start from `snap` (empty if it's null), then apply each sealed change in `ops`, in order. That gives the
  note at version `v`. `sv` is the snapshot's version.
- **Names**: a note's `name` can be null. One the server made after the last note was closed is the case. Show
  `Notes n`, with the lowest n free, worked out the same on every client so they agree.
- **Snapshots**: send `snap` when your own edit's answer reaches a version that's a multiple of 50. On joining, send
  one when a note has 50 or more changes since its snapshot.
- **Checking each change fits**: a change whose kept + removed count doesn't match the note's length, or whose `v`
  isn't one more than the note's, means you're out of step. Send `resync`.
- **A user with no notes yet**: `state` has `notes: []`. The client sends its tabs up as `new`, so the notes someone had
  before using the server aren't lost.
- **Everything else** (`seq`, `stale`, one change in flight, tabs added, closed or renamed while away) works exactly as
  in Daisy's App's API.md. Inside the seals, a change's parts are `[{k: n} | {d: n} | {i: runs}]` and a run is
  `{t, f, a}`, as there.

Daisy's App's `NotepadView.ServerNoteLink` turns these messages into its usual ones, so the rest of its notepad is
unchanged.
