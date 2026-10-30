# Files (server module)

Asked 2026-10-30: a module for a remote client (Android first) to browse the server PC's filesystem, with the shared
drives picked in the server's settings, and everything a client needs to list, get details, preview and open files.
Came in 0.1.1. Messages: PROTOCOL.md §5. Android's side: `Downloads\DaisysServer-Files-Android-Brief.md`.

**Files:**
- `FilesModule.cs`: the module. One question per message (`roots`, `list`, `stat`, `read`, `thumb`, with a `rid`),
  four answered at a time per channel (the rest wait in the channel). `Resolve` checks every path.
- `FilesSettings.cs`: `%APPDATA%\DaisysServer\files.json` (or `DAISYSSERVER_HOME`): `Drives` (roots, `C:\`) and
  `BasicUsers`. Read on every question, so a change counts at once.
- `FilesSettingsView.cs`: its card in Settings (via `IServerModule.SettingsView`, added for this in Core 0.1.1).
- `Thumbnails.cs`: `IShellItemImageFactory` with `SIIGBF_THUMBNAILONLY` on an STA thread; JPEG, or PNG with alpha.

**Decisions (told to the user):**
- **Read-only.** Nothing writes, renames or deletes. Ops for that can be added later (only ever added).
- **Not sealed.** The server reads its own files, so they're only encrypted on the connection; the plaintext rule is
  about users' data kept here.
- **Admins only by default**; **Basic users too** in the card lets the rest in. No drives shared until picked.
- **The client pulls pieces** (`read` with offset and length, at most 1 MB): the session's outbox is unbounded, so the
  server never pushes a whole file. Range reads also give previews and video seeking.
- **Paths:** full, on a shared drive, no UNC/device paths, no `:` past the drive's (streams), no wildcards. Every link
  or junction along the path is resolved, and refused if it leads off the shared drives (C:'s `Documents and
  Settings` junction is fine: it's on C:).
- **No tab:** the Log shows each file opened (read at offset 0); Status shows who has Files open.

**Tested (0.1.1):** TestClient's files checks: roots, list order and paging, stat (mime from the registry), a 300 KB
read in three pieces, past the end, a folder refused, `..`, an unshared drive, UNC, a stream, a relative path, a
junction to an unshared drive, a JPEG thumbnail, no thumbnail for `.bin`. The Settings card on screen.

**Not tested:** a basic user refused; PNG (see-through) thumbnails; videos' and PDFs' thumbnails; a network drive
that's gone; OneDrive placeholders; a hung thumbnail handler (the 15 s replacement thread).
