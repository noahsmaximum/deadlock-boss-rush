# client/ — Source 2 VPK addon (presentation)

The thin client-side layer for everything the server **can't** push to players: custom HUD,
audio, props, map edits. Built with **CSDK 12** on Windows and packed into a `pak##_dir.vpk`.

> **Every player installs this** — an all-in-one VPK via **Deadlock Mod Manager** (or dropped into
> `citadel/addons/`). We run `-insecure` on private servers, so client content mods are fine; this
> is where *all* client-side tweaks live (HUD, audio, particles, and client-visible stat VData).

## Active targets
1. **Hero health regen on the HUD** — define `modifier_bossrush_regen` in `vdata/` with a
   health-regen value; the server applies it (`AddModifier`) and scales it over the match, so the
   on-screen regen number actually moves. (The server `Heal()` loop in `RegenSystem` regens HP but
   can't touch that computed stat — this is the fix.)
2. **`bossrush.ragewave.start`** soundevent — played by `RageWaveSystem` on each rage wave.
3. **`particles/bossrush/patron_laser.vpcf`** — referenced by `BossRushConfig.PatronLaserParticle`
   (currently logs "file not found").

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
- `vdata/` — KV3 stat tweaks. **Client-visible stats belong here**, chiefly a custom
  `modifier_bossrush_regen` (a health-regen value) that the server applies + scales — this is how
  the HUD regen number is moved, since the server `Heal()` loop can't update that computed stat. To
  author it, extract a stock regen modifier with Source 2 Viewer for the exact KV3 schema, then
  rename/retune. Also the home for any hero/item base-stat edits that must be client-visible.

## Build (Windows, CSDK 12)
1. Author sources here. 2. Compile with the Resource Compiler (`*.vdata`→`*.vdata_c`, etc.).
3. Pack the compiled tree into `pak01_dir.vpk` (CS2 Workshop Manager / Multichunk / DeadPacker;
`pak##_dir.vpk`, 01–99, lower number = higher priority). 4. Install via **Deadlock Mod Manager**
(local mod) or by hand to `Deadlock/game/citadel/addons/pve-boss-rush/`, and ensure `gameinfo.gi`
loads addons (`Game citadel/addons` above `Game citadel`). Details:
[`../docs/SETUP.md`](../docs/SETUP.md) §B.

## Notes (from research — see `docs/DESIGN.md` & `docs/VERIFIED_API.md`)
- **Items past 12 work without UI changes** under the Street Brawl ruleset (server-side). You
  cannot add a functional 13th *slot* in XML, but you don't need to — this HUD only *displays*
  the already-equipped extras as a list.
- Custom audio/maps have **no native server→client delivery** — hence the per-player install /
  launcher auto-sync.
- `gameinfo.gi` edits **reset on every major Deadlock patch**; re-apply after updates.
