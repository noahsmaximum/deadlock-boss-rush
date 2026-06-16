# client/ — Source 2 VPK addon (presentation)

The thin client-side layer for everything the server **can't** push to players: custom HUD,
audio, props, map edits. Built with **CSDK 12** on Windows and packed into a `pak##_dir.vpk`.

> ⚠️ **Every player must install this** (the Deadworks launcher can auto-deliver it on
> connect). Keep it as small as possible — push everything else into `server/`.

## What lives here
- `panorama/` — custom HUD. Primary job: the **Relics / Enhancements list panel** that shows
  power held beyond the 16 real item slots (`docs/DESIGN.md` §3 — there is **no** way to add a
  functional 17th item *slot*, so this is a list panel, not fake slots). Optional later: an
  Upgrade-Station reskin of the shop.
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

## Hard limits (from research — see `docs/DESIGN.md`)
- **No 17th item slot.** The inventory is a compiled control driven by server game-state; you
  cannot add functional slots in XML. Show extra power as a list, not slots.
- Custom audio/maps have **no native server→client delivery** — hence the per-player install /
  launcher auto-sync.
- `gameinfo.gi` edits **reset on every major Deadlock patch**; re-apply after updates.
