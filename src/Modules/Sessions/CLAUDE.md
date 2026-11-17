# Sessions (server module)

Asked 2026-11-13: the server side of Daisy's App 4.17.0's SimEffects → Analysis → Share Session, from the brief its
session wrote (`docs/BRIEF-sessions-module.md`), plus an admin tool to find sessions and remove them one at a time or in
bulk, and to ban a cheater's IP, PC or account. 0.3.0: anonymous clients share too (tokens gone). Messages: PROTOCOL.md §7. DA's side:
`src/Modules/SimEffects/Analysis/Sessions/SessionShares.cs` (`SessionServer`) and `ShareWindow.cs`.

**Files:**
- `SessionsModule.cs`: the module: each question answered with its rid.
- `SessionStore.cs`: the index (each session's meta as the `JsonObject` it came as, plus the server's fields and
  `userId` (an anonymous one's `anon:<hash of device>`), `address`, never sent; `anonymous`; old `tokenId`/`token`
  dropped on load), the files (`<id>.gz`), uploads (`uploads\<id>.part`,
  hashed as they come; dropped after 10 minutes, when the channel closes, past 4 at once per channel, and all at
  start), bans (`bans.json`), and the peers pushes go to (posted under the lock: in order). Index written 1 s after a
  change.
- `SessionsPage.cs`: its tab: count and size, Search (every word anywhere in a row), the table (multi-select), Remove
  and Ban user (an anonymous one: their install) / PC / address on the picked ones (Confirm?), the bans with Unban and a box to ban by hand.

**Decisions (mine, told to the user):**
- **A ban stops sharing and editing, not browsing:** the brief's threat is published data, so a banned user, PC,
  address (or CIDR range) can still list and download. Banning removes every session the ban matches. The PC name
  and address are what the client says and where it connects from, so they're easy to change: a user ban is the firm
  one (an anonymous install's isn't: a reinstall is a new one), and deleting the user shuts them out entirely.
- **Anonymous (0.3.0, the user's wish):** `Anonymous => true`; a share is the install's (`device` in hello, hashed),
  `mine`/edit/delete by it; `user` "", `displayName` the PC; the page shows *(anonymous)*. Token bans dropped on load.
- `video` kept only when it's an `https://` link (the client opens it); text clipped (title 200, description 4000).
- A user deleted or reset keeps their shared sessions (they're everyone's): remove or ban them on the tab.
- `IModuleChannel` gained `Address` (0.2.0; `TokenId`/`TokenName` went in 0.3.0).
- Version 0.2.0 (the user asked for the next minor; the brief said 0.1.3).

**Tested (0.2.0):** TestClient's 20 `sessions:` checks (a 700 KB share in two pieces, not listed until complete, the
push to another client with the server's fields set over a spoofed `user`, unknown fields kept, the address not sent,
download identical, get capped, edit and its push, a `javascript:` video dropped, another basic user can't edit or
delete, over 200 MB, a gap, a wrong hash, an unknown op, a banned PC can't share but can list, delete and its push).
The tab on screen through UI Automation (`%TEMP%\dsedit\shot.ps1`): Search, Ban PC on one row removed all three of
that PC's sessions and refused it after, in any case; Remove on one row; Unban; the log lines.

**Not tested:** Daisy's App 4.17.0 against it (the brief's steps 1–6: share, edit, remove, Compare from a second user,
auto-share after YouTube), several rows picked at once on the tab (UI Automation picked only one; it's a normal WPF
Extended DataGrid), Ban user / address and the by-hand ban box (the same code as Ban PC), a 200 MB session.
