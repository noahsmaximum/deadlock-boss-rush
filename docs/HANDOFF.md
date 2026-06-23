# Deadlock BOSS RUSH — Project Handoff & State

> **⚠️ Sections 1–12 + older CURRENT STATE blocks below are STALE snapshots. Read the
> 2026-06-23 + 2026-06-22 blocks first; trust them over everything older where they conflict.** Deep detail
> also lives in agent memory `reference_deadlock-shop-current-state.md` + `reference_csdk-build-pipeline.md`.

---

## CURRENT STATE — 2026-06-23 ✅ MYTHIC ALTAR: LEGENDARY BUY WORKING END-TO-END

Branch `feat/p3-rem-sleep` (committed `60db099`). The **MYTHIC tab** sells the 17 "legendary" items as real store cards; clicking one buys it for a flat **30,000** souls (config `LegendaryPrice`). Server + client both done.

### THE BREAKTHROUGH — how the legendaries got into the store (the niche finding)
- The 23 "legendaries" are **Street Brawl items**: each carries `m_eAbilityRequirements = "ERequirementStreetBrawl"` + `m_iItemTier = "EModTier_5"` (icons under `items/brawl/`). Exactly 23 items in the game data have that requirement = our legendary set. The normal shop **excludes them by tier (only enumerates T1–4)** AND the client **suppresses the buy** (won't even send `buyitem`) because the StreetBrawl requirement isn't met.
- **UNLOCK = two CLIENT `abilities.vdata` edits, BOTH required (either alone fails):**
  1. **Strip the requirement** — delete the `m_eAbilityRequirements = "ERequirementStreetBrawl"` line (23 of them) so the client stops suppressing the buy.
  2. **Relabel tier** — `m_iItemTier "EModTier_5" → "EModTier_4"` (27 lines) so the store *enumerates* them as cards.
  Then **17 of the 23 render** as real, buy-wired store cards in the weapon/armor/tech category lists (the other 6 lack store-card structure). The SERVER keeps reading tier 5 from its own `all_items_tiers.txt` (we only edited the client VPK), so our intercept still recognizes them as legendaries.
- **Enabling Street Brawl mode itself (`citadel_gamemode_streetbrawl_enabled 1`) CRASHES the server** — do NOT. The client-vdata requirement-strip is the safe substitute.

### Server (`BossRushPlugin.OnClientConCommand` — all vetoable concommands)
- `buyitem <T5>` → veto native + flat-charge `LegendaryPrice` (30k) + grant. `buyitem <non-T5>` → blocked (`StoreLegendariesOnly`; loot-only economy).
- `buydependentitem <T5>` = the **imbue legendaries** (only 2: `upgrade_shivas_bracelet`, `upgrade_omnicharge_pendant`). Vetoing skips the imbue, so we let native imbue+charge run, then `Timer.Once(0.2s)` forces the player's **net** gold to `before − LegendaryPrice` (exact 30k regardless of what the engine charged).
- **Removed `citadel_item_purchases_force_enhanced`** (it auto-enhanced native/imbue buys) + the `appear_enhanced` no-op + the convar re-assert timer. Flex slots unlocked in `ApplyRuleset` (`citadel_hero_demo_unlock_flex_slots 1` + `citadel_unlock_flex_slots`).

### Client (`bossrush_probe.js`, MYTHIC tab)
- Reads the **category lists** `ShopModsListWeapon/Armor/Tech` (NOT `ShopModsListAll` — that's the *search* list, empty without a query), filters to the 17 legendaries by display name (`LEGENDARY_NAMES`), reparents those store cards. `selectTab` dispatches all three slots to populate them.
- Forces the card price label to `30,000` and hides the enhance/sell overlays on buy cards; `flipHoverTooltip` clears the stale enhance-flip on unowned cards (the tooltip panel is shared) and relabels legendary cost to 30k.

### Dropped dead-ends (don't retry)
- The **build/favorites view**: `ShopModsSelectedBuild` cards render any tier (no filter) but are **display-only / not buy-wired**; activating `EItemSlotType_Favorites` to wire them **crashes** on shop-reopen (reparenting C++ build cards mid-buy). Abandoned for the store-card path above.
- Tier-relabel *alone* (requirement still present) and requirement-strip *alone* (tier still 5) both fail — needs both.

### POLISH + PERF — ✅ done (2026-06-23, committed `31429fb`)
- **Tooltip price (was the "12,800 on everything" bug):** it was OUR `flipHoverTooltip` injecting an enhance-price row into the SHARED hover tooltip and never updating it (first hovered item's `base×2` stuck for all). Fix: hide the native price on BOTH the card preview (`ItemCost`) and the extended details tooltip (`CitadelTooltipModDetails` → `ModCostContainer`/`ModCost` class — different from `ItemCost`), and show our own row, **updated every hover** — owned `base×2`, legendary flat 30k. Action word reads Buy vs Enhance; enhance arrow (`enhancedIcon`) hidden on buy cards.
- **Unlimited items via the slots-full dialog (no file edits!):** a full-slot buy pops "ITEM SLOTS FULL — select an item to replace"; picking one fires `sellitem(old)` → our intercept **enhances** the old item (not sells) + `buyitem(new)` grants the new legendary → you keep BOTH. We relabel the dialog "ENHANCE A FREE ITEM!" / "Select an item to ENHANCE" (`handleSlotsFullPopup`). So the heroes.vdata slot-cap edit is NOT needed.
- **★ THE SHOP-POLL HITCH (niche, important):** the shop layout **stays active/laid-out in the background** after the first open, so `cStation.actuallayoutwidth` is ALWAYS > 0 → the old gate never read "closed" → the poll did full work + kept native cards borrowed forever → permanent hitch (open or closed). FIX: gate on the **`gShopOpen`** class the engine toggles on the `CitadelHudHeroShop` root (find it by walking parents to `paneltype === "CitadelHudHeroShop"`; the native shop JS uses the same signal). On `!gShopOpen` we also `returnBorrowed()` so the native shop stops churning over the reparented cards while hidden. Also: rebuild the grid ONLY on tab change (no periodic reparent churn), and gate the per-tick list-probe + tooltip traversals.

### Still OPEN / next
- **NEXT TASK: manilla folder tabs** — replace the FAIRWAY/MPS/CURIOSITY/MYTHIC nav buttons with color-matched, dynamically-shaped manilla file-folder tabs (active = raised/colored, inactive = tucked). Needs per-category color theming + tab shape art.
- **Keep native cards** (user requirement): they carry real-time-calculated stats + the starry hover effect; do NOT switch to custom tiles.
- **(optional) `_include` rework** — our abilities.vdata override still ships the whole 6.9MB dump (works, but stale-data risk). Game `abilities.vdata` is tiny + `_include = [ resource_name:"scripts/abilities/<hero>.vdata_inc", … ]`. Could rebuild as header + `_include[base]` + only the ~50 legendary edit lines (re-defined keys override included). Not urgent now that the 12,800 is fixed. `heroes.vdata` has no include array.

---

## CURRENT STATE — 2026-06-22 ✅ UPGRADE STATION ENHANCE: WORKING END-TO-END

Branch `feat/p3-rem-sleep`. The custom **Upgrade Station** shop is functionally DONE: **click an owned item → it enhances** (charge 2× tier price, grant the enhanced variant), fully server-side, one click. The long-standing blocker ("how does a client shop click reach the server") is solved.

### THE KEY BREAKTHROUGH — how the client talks to the server (the niche finding that unlocked everything)
- Panorama has **NO** client→server channel we can call: no console-command runner, no convar setter, no chat-send, no Dota-style custom events. (Confirmed exhaustively; the chat-injection attempt failed — couldn't even fill the chat box.)
- **BUT** the client relays shop/UI actions to the server as **plaintext command strings inside net message `msgId 282 = CM_ClientUIEvent`** (`deadlock/clientmessages.proto`). Clicking an owned item sends **`"sellitem <upgrade_name>"`**, a buy sends **`"buyitem <upgrade_name>"`**; same channel carries `open_item_shop`, `exit_item_shop`, `changeteam 3`, `selecthero hero_x`, `dw_br_loot 10`. The `<upgrade_name>` is the **exact internal catalog id** (e.g. `upgrade_high_velocity_mag`).
- These relay to the server as **concommands**, caught by **`BossRushPlugin.OnClientConCommand(ClientConCommandEvent e)`** — which is **vetoable** (`return HookResult.Stop`). The fix: `if (e.Command == "sellitem")` → veto the native sell + `_upgrades.HandleShopEnhance(caller, e.Args[1])`. **GOTCHA: `e.Args` is argv-style — `Args[0]` is the command name, `Args[1]` is the item.**
- **How we found it (reusable technique):** instrumented Deadworks CORE `EntryPoint.OnNetMessageIncoming` (`C:\deadworks\managed\EntryPoint.cs`) to log every incoming msgId+hex. It's fed by native hook `CServerSideClientBase::FilterMessage` which forwards ALL incoming client→server msgs. Noise: `msgId 4` & `21` are the per-tick flood; rare one-offs are discrete actions; the 282 bytes decode straight to ASCII. (Sniffer has been reverted — re-add it the same way to find any other client action.)

### Server (all in `deadlock-boss-rush/server/BossRush/`)
- `BossRushPlugin.OnClientConCommand` — the `sellitem`→enhance intercept (above).
- `UpgradeStation.HandleShopEnhance` — charge 2× + `GrantTemporaryEnhanced`. **Re-enhance guard:** tracks enhanced items per player (`_enhancedByPlayer`, keyed by controller `EntityIndex`) because the API can't *read* an item's enhanced state — clicking an already-enhanced item would silently re-charge otherwise. Tuned for permanent enhancements (the default).
- **Convars** (asserted on a `Timer.Every(5s)` loop because match-init resets `citadel_*` after `OnStartupServer`): `citadel_item_purchases_force_enhanced 1` (sv, cheat — native BUYS come out enhanced). The display convar `citadel_shop_items_appear_enhanced` is **CLIENT (cl) + cheat + not archived → CANNOT be automated**: server can't set it (engine: "missing required FCVAR flag"), Panorama has no convar API, autoexec is cheat-gated at startup, and an addon-shipped `cfg/autoexec.cfg` does NOT auto-exec (tested — dead). Only the user setting it client-side per session works. **We dropped trying to automate it.**

### Client (`client/panorama/`, mirrored from CSDK content dir)
- Reparent-native-cards architecture (`card.SetParent(tile)`; `#Shop{opacity:0}` keeps data live).
- `bossrush_probe.js`: reads owned items → tiles; **hides the native "OWNED" overlay** (`#ItemPurchased`); relabels the native **"Sell Item?" confirm → "Enhance Item?"** + 2× price (`handleSellPopup`, found by *content* since the `MessageLabel`'s `.text` returns a localization token not the rendered string); flips the hover tooltip Sell→Enhance (`flipHoverTooltip`). Grid rebuilds on tab change **+ every ~2.4s** so enhances/sells refresh instead of lingering.
- **DROPPED (don't re-add):** client-side enhanced detection / inert / "ENHANCED" tag — with `appear_enhanced` on, EVERY card reports `.isEnhanced`, so it false-positived and blocked clicking. Re-enhance protection is server-side instead. Also dropped: the dead chat-send + `IgnoreOwnership` (runtime `AddClass` does NOT change the C++ buy/sell wiring — that's fixed at card construction).

### BUILD PIPELINE — the agent can now drive ALL of it (see `reference_csdk-build-pipeline.md`)
- **Server:** `dotnet build server/BossRush/BossRush.csproj -c Release -p:DeadworksManagedDir="F:\…\Deadlock\game\bin\win64\managed"` → deploys `BossRush.dll` to `managed/plugins` and **hot-reloads** live (no restart). Deadworks SDK source at `C:\deadworks`.
- **Client compile:** `…\game\bin_server\win64\resourcecompiler.exe -f -danger_mode_ignore_schema_mismatches -game "…\game\citadel" -i "<content file>"` (the `-danger…` flag = the GUI's "DANGER ZONE" toggle; without it the compile aborts on schema mismatch).
- **Client pack+deploy:** `…\game\bin\win64\CSDKCfgVPK.exe "<addon folder>" "<out>\pak01_dir.vpk"` (writes in ~3s, doesn't self-terminate → `timeout 6`), then `cp` over the live DMM profile vpk `F:\…\citadel\addons\profile_1781636185610_bor87xr33_pve-boss-rush\pak01_dir.vpk`. **Deadlock must be CLOSED for the vpk swap** (file locked while running); the core `DeadworksManaged.dll` likewise needs the server stopped; only the plugin hot-reloads live.

### Still open / parked (not blockers)
- Pre-enhance enhanced-stats DISPLAY in the shop = only via `appear_enhanced`, which can't be automated (above). Live with base stats + the "Enhance + 2× price" hover, or the user sets the convar/bind themselves.
- Legendary BUY flow (T5 at flat price), manilla tab restyle, Deadlock fonts, hide native HUD bits (souls/quickbuy), rename `bossrush_probe.js`.

---

## CURRENT STATE (2026-06-20) — branch `feat/p3-rem-sleep`

Push: `git push origin feat/p3-rem-sleep`. **`main` moves under us via the user's PR merges — `git fetch`
first; do NOT merge main into the branch unprompted** (a botched merge once half-broke the rotation).

**DONE + verified in-game (server side — feature-complete):**
- **Hidden King boss fight:** phase-aware ult rotation (Phase 1 Laser + Rocket Barrage; Phase 2 unlocks
  Seven Storm Cloud + Bomb Blast), real shipped particles/sounds, stun (`modifier_citadel_knockdown`),
  knockback (`AbsVelocity`), charge-up Bomb Blast, health-pool preserved across phase resets, tuned damage.
  Heavy kit unlocks by **bars-lost** (`_heavyKitUnlocked`), not the health-wiping native phase-2 cvar.
- **World loot:** breaking boxes → items. Detected via **OnEntitySpawned** of the drop pickups
  (`citadel_breakable_prop_gold_pickup` / `_modifier_pickup`) — props fire neither damage nor touch hooks.
  **Time-tiered rarity** (items bucketed by `m_iItemTier`; weights by match clock). Crates/breakables
  front-loaded + fast respawn via `citadel_*` convars. `LootDropChance` 0.6.
- **Store backend:** `dw_br_enhance <held>` (2× tier price), `dw_br_buylegendary <T5>` (flat 25000).
  Permanent enhancements. (The shop *UI* is the next task.)
- **Lane defenses:** Walker ×4 HP; guardian HP buffs. (Adding a 2nd guardian = map edit; raw spawn crashes.)

**CLIENT MOD (CSDK 12) — pipeline PROVEN:** author source in `Reduced_CSDK_12\content\citadel_addons\
pve-boss-rush\`, user compiles in Asset Browser (`csdkcfg.exe`) → packs VPK (`CSDKCfgVPK.exe`) → installs
via DMM. Mirrored to repo `client/`.
- **Data overrides load + recompile** (proven: generic_data price + `abilities.vdata` non-hero proc fix,
  verified). Overriding `abilities.vdata`: **delete the `_include` block** first (decompiler inlines content).
- **Proc lesson:** `CITADEL_UNIT_TARGET_ALL_ENEMY` ≈ enemy HEROES only; full enemy set =
  `HERO_ENEMY|TROOPER_ENEMY|BOSS_ENEMY|BUILDING_ENEMY|PROP_ENEMY|MINION_ENEMY|NEUTRAL|CREEP_ENEMY`.
  Proc/ability behavior is **server-authoritative** (DMM addon is client-side; use loose-file override
  `citadel/scripts/X.vdata_c` for a separate dedicated server).
- **Panorama:** overrides LOAD, but **decompiled panorama is LOSSY** (a recompiled layout broke the prompt).
  Shop logic is **C++** (`CitadelShopMods*` panels; almost no JS; schema JSON doesn't cover UI panels).
  Shop layouts: `citadel_hud_hero_shop` (container), `citadel_shop_mods_tier` (tier row), `citadel_shop_mod_view`
  (item card w/ built-in Enhance), `citadel_shop_mods_build`. Banner likely an image.

**NEXT: CUSTOM UPGRADE-STATION UI (client Panorama).** User chose to attempt custom layouts. Iterative
compile→look→fix grind: reskin via `.vcss`, override layout shells per-file (testing each, lossy-aware),
keep C++ panels, lean on native Enhance + `dw_br_*` commands. Other parked client-mod items: imbue picker,
no-buy-T1-T4 enforcement, Rem sleep modifier (server VPK), boss HUD.

---

## 1. The goal

**Boss Rush** is a custom **PvE co-op** game mode for **Deadlock**: all human players are on the
**Archmother** team and fight the AI **Hidden King** side — escalating lane-trooper waves, periodic
"rage wave" floods, Guardians/bosses, loot, an Upgrade Station, and the **Patron** as the finale
(killing it wins). It is built as a **Deadworks server plugin** (C# / .NET 10) plus a **client VPK
addon** for anything that must be client-side (HUD/UI, custom particles/sounds, stat VData).

We run on **private servers with `-insecure`**, so client content mods are acceptable.

---

## 2. Architecture (two deliverables that ship together)

| Piece | Source | Output | Loads from |
|---|---|---|---|
| **Server plugin** | `server/BossRush/` (.NET 10) | `BossRush.dll` | `…\Deadlock\game\bin\win64\managed\plugins\` (Deadworks scans it, hot-reloads on change) |
| **Client addon** | `client/` | `pak01_dir.vpk` | `…\Deadlock\game\citadel\addons\pve-boss-rush\` (Deadlock Mod Manager / manual) |

**Contract:** the server references client assets/modifiers **by name** (e.g. `bossrush.ragewave.start`,
`particles/bossrush/patron_laser.vpcf`, the planned `modifier_bossrush_regen`). The VPK must define
them with matching names — keep them in sync with `BossRushConfig`.

The mode runs on top of a **normal Deadlock match** (`mode=Invalid`, `state=GameInProgress`); our
plugin layers the PvE behavior on. Deadworks is the C++/C# framework that injects the plugin
(`deadworks.exe` launches the dedicated server and loads managed plugins).

---

## 3. Data locations (Noah's machine)

| Path | What it is |
|---|---|
| `C:\Users\Noah\Projects\deadlock-boss-rush-main\deadlock-boss-rush-main` | **The repo clone** — where `BossRush.dll` is built. (GitHub: `noahsmaximum/deadlock-boss-rush`, branch `claude/affectionate-volta-nw1it8`.) |
| `C:\Users\Noah\Projects\deadlock-boss-rush-main\Deadworks BOSS RUSH Server Start.bat` | **The launcher** — runs `deadworks.exe` with the full arg line + `-condebug` + `+map`. |
| `F:\I Drive\SteamLibrary\steamapps\common\Deadlock` | **The game install.** |
| `C:\deadworks` | **The Deadworks clone** — builds `deadworks.exe` and the managed SDK (`DeadworksManaged.Api.dll`). A clean clone of `Deadworks-net/deadworks` + our `local.props` + build outputs. |

**Key derived paths (under the Deadlock install):**
- Server exe: `F:\I Drive\SteamLibrary\steamapps\common\Deadlock\game\bin\win64\deadworks.exe`
- Managed SDK + plugins: `…\game\bin\win64\managed\` (has `DeadworksManaged.Api.dll`; **`Google.Protobuf.dll` must be here too** — see §6) and `…\managed\plugins\BossRush.dll`
- **Signature config (runtime):** `…\game\citadel\cfg\deadworks_mem.jsonc`
- **Console log** (`-condebug`): `…\game\citadel\console.log`
- Entity dumps (`dw_br_dumpents`): `C:\Users\Noah\deadlock_dumps\`
- Client addon install target: `…\game\citadel\addons\pve-boss-rush\pak01_dir.vpk`

---

## 4. Build & run

**Build the plugin** (standalone, from the repo clone — auto-deploys `BossRush.dll` to `…\managed\plugins\`):
```cmd
dotnet build server\BossRush\BossRush.csproj -c Release ^
  -p:DeadworksManagedDir="F:\I Drive\SteamLibrary\steamapps\common\Deadlock\game\bin\win64\managed"
```
(or set env `DEADWORKS_MANAGED_DIR` once and just `dotnet build …`). The csproj also supports the
in-tree "junction" layout (`mklink /J <deadworks>\examples\plugins\BossRush <repo>\server\BossRush`,
build from there) — but standalone is simplest.

**Run the server** — use the `.bat`. It passes the FULL default arg line **plus** `-condebug`:
```
deadworks.exe -dedicated -console -dev -insecure -allow_no_lobby_connect ^
  +tv_citadel_auto_record 0 +spec_replay_enable 0 +tv_enable 0 +citadel_upload_replay_enabled 0 ^
  +hostport 27067 -condebug +map dl_midtown
```
⚠️ The launcher **replaces ALL its built-in args the moment you pass any** (`startup.cpp`) — so a
partial arg line breaks it (no `+map` ⇒ never loads). Always pass the whole line.

**Connect:** launch Deadlock, console: `connect localhost:27067`. **Pick the Archmother side at hero
select** (team-forcing is disabled — see §6). Plugin hot-reloads when you rebuild `BossRush.dll`.

---

## 5. Current status

### ✅ Works (verified live)
- Plugin loads; Deadworks registers all hooks + commands; hot-reload on rebuild.
- All dev commands (§8) function; `dw_br_dumpents` dumped 3431 entities / 99 designer names.
- **Lane-based enemy waves** via `citadel_spawn_trooper_zipline` — Hidden King troopers march from
  their lanes (left/mid/right), reinforced ~2× the Archmother (`SpawnDirector`).
- **Rage waves** (`RageWaveSystem`) — HUD announcement + lane floods (`dw_br_ragewave` to trigger),
  squad scales 10 → 15@20min → +5/10min.
- **Custom hero regen** (`RegenSystem`) — `Heal()` loop, hero-only, scales 50→200 HP/s over 30 min.
  Confirmed healing works (`dw_br_heal`: 144→344). **Caveat:** does NOT move the on-screen regen
  number (that's a computed stat), and currently pauses 5s after damage (likely too long — see §10).
- **The Patron**: spawning `npc_boss_tier3` and killing it **wins the match** (native).

### 🔶 Stubbed / unverified (written but not made real yet)
- `PatronCombatSystem` — Patron laser/buff loops; references a missing `patron_laser.vpcf`. (Note the
  native tier-3 boss already has `citadel_ability_tier3boss_laser_beam` + `…_aoe_wave`.)
- `LootSystem`, `UpgradeStation`, `EnhancementSystem` — compile but not exercised/confirmed in-game.
- Enemy trooper **health buff** (`SpawnDirector.OnEntitySpawned`, 2× scaling) — wired but "tankier"
  not yet visually confirmed by the player.

### ❌ Not solved
- **Team lock** ("Hidden King unselectable") — `ChangeTeam()` on a live player **breaks them**
  (stuck, no hero load). Auto-forcing removed. Needs a non-`ChangeTeam` mechanism (client-side
  team-select lock / co-op setup). For now: pick Archmother manually.
- **HUD regen number** — no settable field/cvar; needs the client VPK (custom modifier). See §10.

---

## 6. Critical gotchas & prerequisites (these MUST hold or the server won't run)

1. **Deadworks signature patch.** Deadworks' sigs lag Deadlock patches. The June-11 build broke
   `CCitadelPlayerPawn::AbilityThink`. In `…\game\citadel\cfg\deadworks_mem.jsonc`, that signature's
   `windows` pattern must be the community-PR value:
   `40 55 53 41 54 41 55 41 57 48 8D AC 24` (the stale one was `48 89 4C 24 ?? 55 53 56 41 55 41 57`).
   **Re-apply if `deadworks_mem.jsonc` is regenerated or Deadworks/Deadlock updates.**
2. **`Google.Protobuf.dll` must be in `…\game\bin\win64\managed\`.** Deadworks' deploy omits it;
   without it the server crashes at map load with an unhandled `System.IO.FileNotFoundException`
   (`Google.Protobuf 3.29.3.0`) — minidump exception code `0xE0434352` (a CLR managed exception, not
   a native AV). Copy it from the NuGet cache (`%USERPROFILE%\.nuget\packages\google.protobuf\3.29.3\lib\net8.0\`)
   or the managed build output.
3. **Console commands are prefixed `dw_`** (e.g. `dw_br_dumpents`); chat commands are `!name`. Some
   commands need a connected player (run them in-game), others are server-console.
4. **`sv_cheats 1`** is set by `ApplyRuleset` (the trooper spawners are cheat-gated).
5. **Build from the right place** — the csproj SDK reference is relative; build via the standalone
   `-p:DeadworksManagedDir` flag or the in-tree junction, not a loose copy.

---

## 7. Verified facts (the live, confirmed truth — see also `docs/VERIFIED_API.md`)

- **Teams:** `2 = Hidden King` (AI enemies), `3 = Archmother` (heroes). Constants in `BossRushPlugin`:
  `HeroTeam = 3`, `EnemyTeam = 2`.
- **Hidden King lanes** for `citadel_spawn_trooper_zipline <team> <lane>`: **`1` = left, `4` = middle,
  `6` = right** (config `HiddenKingLanesCsv = "1,4,6"`).
- **NPC spawn results** (via `CreateByDesignerName` + `Spawn`):
  - `npc_boss_tier3` = **the Patron** (spawns, attacks, **killing it wins**; has native laser + AoE).
  - `npc_barrack_boss` = **Guardian** (spawns clean).
  - `npc_neutral_bug` = denizen (spawns at 1 hp; the locked-in-place neutral camp creep).
  - `npc_trooper` = lane trooper — **raw spawn CRASHES the server** (native AV; needs lane context).
    Use the console spawner instead.
- **Lane trooper spawn commands** (cheat-gated; run via `Server.ExecuteCommand` server-side):
  `citadel_spawn_trooper` (in front of player), `citadel_spawn_trooper_grid <N>`,
  `citadel_spawn_trooper_zipline <team> <lane>` (the lane-based one we use). Cleanup:
  `trooper_kill_all`, `citadel_destroy_all_npcs`.
- **Wave-tuning cvars** exist (`citadel_trooper_squad_size`=4, `…_spawn_interval_early/late`=30/25,
  `…_health_mult`=1.5, `…_spawn_enabled`).
- **`Heal()` works** on hero pawns; setting `Health`/`MaxHealth` works. The **regen HUD stat does
  not** have a settable field; `sv_regeneration_force_on` has **no rate cvar**;
  `citadel_1v1_bonus_health_regen` is 1v1-gated (no effect here).
- **`npc_create`** spawns idle denizens (not fighting lane troopers) — wrong for waves.
- `dl_midtown` idle entity census highlights: `citadel_breakable_prop` (×691, loot urns),
  `info_trooper_spawn`, `npc_barrack_boss` (×12 Guardians), `trigger_item_shop` (×9), `info_team_spawn`.

---

## 8. Dev command reference (all `dw_br_*` in console, or `!br_*` in chat)

| Command | Does |
|---|---|
| `dw_br_dumpents [path]` | Dump every entity (designer name, team, pos, hp) to JSON + count summary. |
| `dw_br_nearby [radius]` | List entities near you, closest first. |
| `dw_br_pos` | Print your position + camera angles as JSON. |
| `dw_br_gamestate` | Print GameRules: mode/state/clock/midboss & rejuv counts. |
| `dw_br_spawn <designer> [team] [hp]` | Spawn one entity in front of you (refuses `npc_trooper`-family which crash). |
| `dw_br_cmds <substr>` | List convars/concommands matching a word (how we found the trooper spawners). |
| `dw_br_run <cmd> [args…]` | Run a server console command w/ `sv_cheats` (e.g. `dw_br_run citadel_spawn_trooper_zipline 2 1`). |
| `dw_br_ragewave` | Trigger a rage wave now. |
| `dw_br_heal <amount>` | Test-heal yourself; reports `Heal()` vs direct-set (diagnostic). |
| `dw_br_additem <name> [enhanced]` | Give yourself an item by internal name (to probe HUD stats). |

---

## 9. Config knobs (`server/BossRush/BossRushConfig.cs` — `[PluginConfig]`, editable + `dw_reloadconfig`)

- **Spawning:** `HiddenKingLanesCsv="1,4,6"`, `ReinforcementIntervalSeconds=30`, `ReinforcementSquadSize=4`
  (per lane), `DenizenBaseStrengthMultiplier=2.0`, `DenizenStrengthPerMinute=0.04`.
- **Regen:** `RegenStartPerSecond=50`, `RegenMaxPerSecond=200`, `RegenRampMinutes=30`, `RegenWaitSeconds=5`.
- **Rage waves:** `RageWaveIntervalMinutes=10`, `RageWaveSurgeDurationSeconds=60`,
  `RageWaveSpawnIntervalSeconds=20`, `RageWaveSquadBase=10`, `RageWaveSquadStep=5`,
  `RageWaveSquadFirstStepMinute=20`, `RageWaveSquadStepMinutes=10`, `RageWaveStartSound="bossrush.ragewave.start"`.
- **Patron:** `PatronLaserBaseDamage=60`, `PatronLaserDamagePerMinute=8`, `PatronLaserIntervalSeconds=3.5`,
  `PatronLaserParticle="particles/bossrush/patron_laser.vpcf"`, `PatronLaserSound="bossrush.patron.laser"`,
  `PatronBuffRollIntervalSeconds=30`.
- **Economy:** `UpgradeCostMultiplier=2.0`, `EnhancedDropChance=0.01`, `EnhancementDurationSeconds=300`,
  `GuardiansPerLaneMultiplier=2`.

---

## 10. The journey (challenges solved, in order)

1. **Sig mismatch** — June-11 Deadlock broke Deadworks' `AbilityThink` sig. Confirmed `C:\deadworks`
   is a clean clone (we only added `local.props`, build outputs, the BossRush junction). v0.4.10
   release had the same stale sig. Fixed by applying the community-PR pattern (§6.1).
2. **Startup crash** — minidump (`0xE0434352`) decoded to a missing `Google.Protobuf.dll` (§6.2).
3. **"Commands unknown"** — learned the `dw_` prefix + the launcher arg-replacement quirk; added
   `-condebug` logging.
4. **Build errors from a loose copy** — made `BossRush.csproj` support standalone builds via
   `DeadworksManagedDir`.
5. **"Just a normal game"** — gameplay systems were hollow stubs; began making them real.
6. **Spawn experiments** — found `npc_boss_tier3`=Patron(win), `npc_barrack_boss`=Guardian,
   `npc_trooper` crashes; discovered the `citadel_spawn_trooper*` commands via `dw_br_cmds`.
7. **PvE pivot** — all players → Archmother; enemies = Hidden King's natural + reinforced lane
   troopers. **`ChangeTeam` breaks live players** → auto-forcing removed.
8. **Lane spawning** — `citadel_spawn_trooper_zipline 2 <lane>`; lanes 1/4/6; per-lane reinforcement
   (~2× Archmother) + enemy health buff; rage waves became lane bursts.
9. **Regen** — `sv_regeneration_force_on` has no rate cvar; built a custom `Heal()` loop (scales
   50→200). Works but can't move the HUD number → motivated client VPK.
10. **Client VPK decision (current)** — embrace client installs; all-in-one VPK via Deadlock Mod Manager.

---

## 11. Up next (roadmap)

**Immediate — client VPK (needs Source 2 / Citadel SDK + Source 2 Viewer on Windows):**
1. **Hero regen on the HUD** (the motivator). Plan: define **`modifier_bossrush_regen`** in
   `client/vdata/` with a health-regen value; server applies it via `AddModifier` and scales it, so
   the on-screen regen stat actually moves. Step 0: extract a stock health-regen modifier with
   Source 2 Viewer to get the exact KV3 schema, then rename/retune.
2. **`bossrush.ragewave.start`** soundevent (`client/soundevents/`) — currently silent.
3. **`particles/bossrush/patron_laser.vpcf`** — currently "file not found".
4. **UI/HUD** (Panorama) — "owned items" list (items past 12 slots), wave counter, objective banner.
5. Set up CSDK toolchain + a repeatable VPK build/pack step; install via Mod Manager.

**Gameplay tuning / finishing:**
- **Lower `RegenWaitSeconds`** (5 → 1–2) — at 5s the regen barely fires during a horde fight.
- Confirm the enemy **health buff** is visible (HK troopers tankier than Archmother's).
- Make the **Patron finale** real (spawn `npc_boss_tier3` as the climax; it already wins on death).
- Flesh out `LootSystem` / `UpgradeStation` / `EnhancementSystem` against the live game.
- Revisit **team lock** with a client-side mechanism (not `ChangeTeam`).

---

## 12. Repo map

- **Branch:** `claude/affectionate-volta-nw1it8` (GitHub `noahsmaximum/deadlock-boss-rush`) — all work pushed here.
- `server/BossRush/BossRushPlugin.cs` — entry point: `OnLoad`/`OnStartupServer`/`OnUnload`, `ApplyRuleset`,
  event hooks (`player_death`, `OnTakeDamage`, `OnEntityStartTouch`, `OnEntitySpawned`), `dw_upgrade`.
- `server/BossRush/Systems/` — `SpawnDirector` (HK reinforcements + enemy buff), `RageWaveSystem`
  (lane bursts), `RegenSystem` (Heal loop), `LaneTroopers` (zipline spawn helper), `PatronCombatSystem`,
  `LootSystem`, `UpgradeStation`, `EnhancementSystem`, `DevTools` (partial class — all `dw_br_*` commands).
- `server/BossRush/BossRushConfig.cs` — all tunables (§9).
- `client/` — VPK addon source (`vdata/`, `soundevents/`, `panorama/`) + `README.md` (build/install/targets).
- `docs/` — `DESIGN.md` (mode design), `VERIFIED_API.md` (source-verified Deadworks API + live findings),
  `SETUP.md` (build/deploy), and **this `HANDOFF.md`**.
