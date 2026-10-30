# Notepad (server module)

**Files:**
- `NotepadModule.cs`: the module, with one `Hub` per user.
- `Hub.cs`: that user's notes and the clients joined. Kept in `notes.json` in the user's folder, written 1 s after a
  change.
- `NotepadPage.cs`: its tab, with each user's counts and sizes (the text is sealed, so it isn't shown).

**Messages:** PROTOCOL.md §4. They are DA's notepad channel with every name, note and change sealed, plus `snap`
(clients send one every 50 versions) and `resync`.

**How it handles notes:**
- An edit is accepted only when its `base` matches the note's version; otherwise the client gets `stale`. This is the
  only check possible, since the server can't read the text.
- Closing the last note makes an unnamed one (`name: null`); clients name it `Notes n`.
- An empty account sends `state` with `notes: []`.

**DA's counterpart:** `NotepadView.ServerNoteLink` in DaisysApp's `src/Modules/Tools/Notepad/`. Android's is planned,
per `Downloads\DaisysServer-Android-Brief.md`.

**0.1.0 tests:**
- TestClient: new, edit, stale, rename, close, who, snap.
- Two DA copies: tabs went up, `poke` edits and renames showed, an offline edit merged.

**Not done:**
- No cap on the change log when clients don't send snapshots.
- Nothing is cleaned up when a client sends a bad change; clients `resync` when they notice one.
