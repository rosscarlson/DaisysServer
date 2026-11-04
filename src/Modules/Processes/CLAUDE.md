# Processes (server module)

Asked 2026-11-04: the master database for Daisy's App 4.1.0's Support → Process Analyzer, built from the brief its
session wrote (in REQUESTS.md). Messages: PROTOCOL.md §6. DA's side: `src/Modules/Support/ProcessAnalyzer/ServerSync.cs`
and `ProcessDb.cs` (the entry, `KnownProcess`, and the client's merge).

**Files:**
- `ProcessesModule.cs`: the module (one `ProcessDatabase`, a `Peer` per channel).
- `ProcessDatabase.cs`: the entries (each kept as the `JsonObject` it came as, so unknown fields survive), the version,
  the merge, the PCs and their usage, export and import. `data\processes\processes.json` and `clients.json` (under
  `JsonFile.DataFolder`, not a user's folder), written 1 s after a change. One lock; what goes to clients is posted
  under it, so every client gets every change in order (that's why a client's version is always safe to trust).
- `ProcessesPage.cs`: its tab: version, Export…, Import…, the PCs (behind or up to date), the entries with a search.

**Decisions (the brief's, kept):**
- **Not sealed, one database for every user** (the user asked for it on 2026-11-04). Any signed-in user can report.
- **The merge** is the client's: none kept → take; `reviewed` differs → the reviewed one; else the newer `updated`
  (a tie keeps the kept one). The winner replaces the entry whole (so a field the old one had and the new one lacks
  goes). `rev` and `fleet*` from clients are dropped.
- **A report's answer:** the changed entries go to every joined client with one new version; the sender's copy adds the
  server's version of the ones that didn't change. Nothing changed: only the sender, same version.
- **Join:** `rev > version`, or everything (`full`) when `version` is 0 or past the server's. No entry is ever deleted,
  so history is never dropped and that's the only full case.
- **Fleet figures:** each PC's latest usage per program (30 days), averaged on the entry when usage comes in. They go out
  with the entry's next change, and the entries whose figures changed get a version of their own once a day (when usage
  arrives more than a day after the last time: no timer).
- **PCs** are keyed by the connection's client name (DA: the PC's name), so two DA copies on one PC share a row.
- No editing on the server (the brief's optional part), and no deleting an entry (no op for it).

**Tested (0.1.2):** TestClient's 13 `processes:` checks (full on join, nothing for an up-to-date client, a report
numbered and sent to both, unknown fields kept, a client's fleet figures dropped, a tie, reviewed beats automatic and
isn't replaced by a newer automatic one, catching up, `seen` kept, another server's version gets everything, fleet
averages), and `TestClient report` as a third PC. Two DA 4.1.0 copies (`%TEMP%\dsedit\make_devp.ps1`, Support only,
in the tray): the first's automatic entries went up (9, version 6), the second got everything on joining (11, full), a
reviewed edit from TestClient reached both within 4 s, the second stopped while two edits were made caught up on
joining (2 sent). The tab on screen.

**Not tested:** Export… and Import… (file dialogs), the daily fleet broadcast after a day, a big database (thousands of
entries: one `changes` frame up to 32 MB, about 50,000 entries), several users.
