# client/ — Source 2 VPK addon (presentation)

The thin client-side layer for everything the server **can't** push to players: custom HUD,
audio, props, map edits. Built with **CSDK 12** on Windows and packed into a `pak##_dir.vpk`.

> ⚠️ **Every player must install this** (the Deadworks launcher can auto-deliver it on
> connect). Keep it as small as possible — push everything else into `server/`.

## What lives here
- `panorama/` — **optional** custom HUD. Items past the 12 visible slots are *already equipped
  and functional* under the Street Brawl ruleset (`docs/DESIGN.md` §3); the only gap is that the
  stock HUD only draws 12. So this panel is an **"owned items" list** that *shows* everything
  held — not fake functional slots. Optional later: an Upgrade-Station reskin of the shop.
  - Deadlock UI is Valve **Panorama** (`*.xml` layouts, `*.vcss` styles, `*.vjs` scripts).
    Decompile shipped UI with Source 2 Viewer for reference; the "CSS hijack" technique
    (`@import` the base style, override) avoids touching layout where possible.
- `soundevents/` — `*.vsndevts` declaring the custom soundevents the server triggers, e.g.
  `bossrush.ragewave.start`, `bossrush.patron.laser`. Source WAV/MP3 → compile to `.vsnd`.
- `vdata/` — *optional* KV3 overrides for NPC/item stats (alternative/supplement to runtime
  modifiers). Server-authoritative, so these only need to exist on the server — keep them out
  of the client VPK unless a specific change must be client-visible.

## Build (Windows, CSDK 12)
1. Author sources here. 2. Compile with the Resource Compiler. 3. Pack the compiled `game`
tree into a VPK (CS2 Workshop Manager / Multichunk / DeadPacker). 4. Install to
`Deadlock/game/citadel/addons/` and add `Game citadel/addons` above `Game citadel` in
`gameinfo.gi`. Details: [`../docs/SETUP.md`](../docs/SETUP.md) §B.

## Notes (from research — see `docs/DESIGN.md` & `docs/VERIFIED_API.md`)
- **Items past 12 work without UI changes** under the Street Brawl ruleset (server-side). You
  cannot add a functional 13th *slot* in XML, but you don't need to — this HUD only *displays*
  the already-equipped extras as a list.
- Custom audio/maps have **no native server→client delivery** — hence the per-player install /
  launcher auto-sync.
- `gameinfo.gi` edits **reset on every major Deadlock patch**; re-apply after updates.
