# Deadlock Boss Rush — Design & Feasibility

A co-op **PvE** mode for Valve's *Deadlock*: a team of heroes spawns on one side of
the map and fights through three lanes — defended by **double the usual Guardians/towers**
and an actively-fighting **Patron** — looting power from the world instead of buying from
a shop. Culminates in killing the Patron.

This document is the source of truth for *what we're building*, *how it maps onto what
Deadlock modding actually allows*, and *the order we build it in*.

> **Status:** design + scaffolding. The gameplay code targets the community **Deadworks**
> server framework. Its APIs were **pinned by reading the actual SDK source** (see
> `docs/VERIFIED_API.md`), but Deadworks is *"early development… changes without notice"* and
> the scaffold hasn't been compiled/run yet — a few items still need a live server (VERIFIED_API.md §9).

---

## 1. The two-layer architecture (read this first)

Deadlock has **no official mod SDK and no custom-games system** like Dota 2. Everything
here rides on community tooling, and it splits cleanly into two layers:

| Layer | What it is | What it runs | Player install? |
|---|---|---|---|
| **Server mod** | A C# plugin for **[Deadworks](https://github.com/Deadworks-net/deadworks)**, a framework that runs a custom **dedicated server** with full C# access to the live game. | All *gameplay logic & data*: spawns, NPC strength, the Patron's attacks, the economy, item grants, modifiers, waves, timers. | **No.** Server-authoritative. |
| **Client addon** | A Source 2 **VPK** built with **[CSDK 12](https://deadlockmodding.pages.dev/modding-tools/csdk-12)** (Hammer, Panorama, asset compilers). | All *presentation*: custom HUD panels, the rage-wave audio, custom props/models, any map geometry edits. | **Yes — every player** installs it (the Deadworks launcher auto-delivers it on connect). |

**Consequence that drives the whole design:** Deadlock servers cannot push content to
clients on their own. Anything visual/audio/map must be a client mod that every player
has. So we push *as much as possible* into the server layer (zero install) and keep the
client addon as thin as we can.

**How players join:** a custom **dedicated server** (not Valve matchmaking). Either the
**Deadworks launcher** (browse server list → one-click connect, auto-syncs the client
addon) or console `connect <ip>:27067`. See `docs/SETUP.md`.

---

## 2. Feasibility matrix — every mechanic in the pitch

Legend: ✅ supported · 🟡 partial / hand-built · ⛔ blocked (needs design change).
"Layer" = where the work lives. "Install" = does it force a client-side mod.

| # | Your mechanic | Approach | Layer | Verdict | Install |
|---|---|---|---|---|---|
| 1 | Push 3 lanes, **2× Guardians** to the Patron | Spawn duplicate Guardian entities at runtime (`CBaseEntity.CreateByName` + position), **or** place them in an edited map | Server (or client map) | 🟡 | No (runtime) |
| 2 | **Patron attacks back** — lasers, scaling stronger, random buffs | No "fire laser" API, but assemblable: `Hurt`/`TakeDamage` + `CParticleSystem` beam + `EmitSound`, on a scaling `Timer`; "random buffs" = `AddModifier` | Server | 🟡 (hand-built) | No |
| 3 | Reach Patron by killing towers, then **defeat the Patron** | Standard objective flow; hook `OnTakeDamage`/death | Server | ✅ | No |
| 4 | Items found in **golden buddhas, boxes, etc.** (+ normal souls & powerups) | Hook crate-break / proximity-touch on existing **gold statues / `item_crate`** → `CCitadelPlayerPawn.AddItem(name)`. Souls/powerups stay native | Server (+ optional custom model) | 🟡 | No (custom model = yes) |
| 5 | **Shop → Upgrade Station**: buy enhanced versions of items you hold, at **2× price** | **There is no shop-catalog API.** Build the mechanic instead: detect player at a station zone → offer upgrades via HUD/command → `ModifyCurrency` (−2× cost) → swap to **`AddItem(name, enhanced: true)`** (native "enhanced" flag!) | Server (+ optional shop-UI reskin) | 🟡 | No (reskin = yes) |
| 6 | Enhanced items also **findable, extremely rare** | Loot roll → `AddItem(name, enhanced: true)` at a tiny drop weight | Server | ✅ | No |
| 7 | **No item limit** / hold items beyond the 12 visible slots | Base the mode on the **Street Brawl ruleset** (`ECitadelGameMode.StreetBrawl`), which since the **Mar 6 2026 patch** has **no item-slot limit** — items past the 12 visible stay equipped & functional, just hidden in the HUD. `AddItem` keeps granting them. The *only* gap is **seeing** >12, fixed by an optional custom HUD list. See §3. | Server (+ optional HUD) | ✅ (functional) | Only to *show* >12 |
| 8 | Enhancements last **5 min or until death** | `AddModifier` with `duration` KV (or self-managed `Timer` + `RemoveModifier`); clear on `player_death` | Server | ✅ | No |
| 9 | Heroes' troopers normal; **enemies 2× spawn rate** | Inject extra enemy trooper waves at runtime on a timer (cleaner than symmetric VData edits) | Server | 🟡 | No |
| 10 | **Denizens stronger + scaling** over time | Buff base stats (runtime modifiers, optionally VData) and scale on a timer to track hero power | Server (+ optional VData) | ✅ | No |
| 11 | **More crystal-buff spawns** (3 for mid-boss, one under each bridge lane, a few underground for small heroes) | Spawn rejuv/crystal pickups at extra coords at runtime; *underground/under-bridge* spots that need new geometry require a **map edit** | Server (+ client map for new geometry) | 🟡 | Geometry = yes |
| 12 | **Rage wave** every 10 min: **4× troops** each side, hectic until cleared, custom audio | `Timer.Every(10.Minutes())` → spawn 4× troopers → track entities until count 0 → `EmitSound(custom)` + `HudGameAnnouncement`; **custom audio asset is client-side** | Server + client audio | ✅ (logic) | Audio = yes |

### Confirmed-supported SDK building blocks (from actual Deadworks source/examples)
- **Lifecycle & events:** `DeadworksPluginBase` (`Name`, `OnLoad`, `OnUnload`, `OnStartupServer`, `OnPrecacheResources`), `[GameEventHandler("player_death" | "player_respawned" | …)]`, virtual hooks `OnTakeDamage`, `OnEntitySpawned/Created/Deleted`, `OnEntityStartTouch/EndTouch`, `OnGameFrame` (per-tick), `OnAddModifier`, `OnModifyCurrency`, connect/disconnect hooks — most return `HookResult` so you can intercept.
- **Entities:** `CBaseEntity.CreateByName/CreateByDesignerName` → `Spawn(CEntityKeyValues)`, `Position`/`Teleport`, `SetModel/SetScale`, `Health/MaxHealth`, `TeamNum`, `AcceptInput(...)`, `Remove()`. Query: `Entities.ByClass<T>()/ByName/ByDesignerName`.
- **Players:** `CCitadelPlayerPawn.AddItem(name, enhanced=false)`, `RemoveItem`, `SellItem`, `AddAbility`, `Level`, `GetCurrency/SetCurrency/ModifyCurrency`.
- **Combat:** `OnTakeDamage(TakeDamageEvent)→HookResult` (read/rewrite `Damage`, `DamageType`, attacker…); deal damage via `Hurt(...)` / `TakeDamage(CTakeDamageInfo)`.
- **Modifiers:** `AddModifier(name, KeyValues3{duration…})` / `RemoveModifier`.
- **Feedback:** `NetMessages.Send(CCitadelUserMsg_HudGameAnnouncement{…}, RecipientFilter)`, `Chat.PrintToChat`, `CParticleSystem.Create(...).AtPosition().Start()`, `CBaseEntity.EmitSound(name)`.
- **Scheduling:** `Timer.Once/Every/Sequence/NextTick`, `N.Seconds()/Minutes()/Ticks()`.
- **Game state:** `GameRules` exposes `MidbossKillCount`, `NextMidBossSpawnTime`, `AmberRejuvCount`, `SapphireRejuvCount`.

---

## 3. "Unlimited items" — solved via the Street Brawl ruleset

Earlier research treated >16 items as a hard wall. **A correction from playtest knowledge flips
that:** Deadlock's official **Street Brawl** mode has had **no item-slot limit since the
March 6 2026 patch** — when you hold more than the 12 visible slots, the *oldest/least* items
simply drop off the HUD while **staying fully equipped and providing their stats**. This is a
real engine capability, and the SDK is aware of it:
- `ECitadelGameMode.StreetBrawl = 4` is a first-class mode; `GameRules.GameMode` reads it live.
- Items carry a separate **`m_strStreetBrawlValue`** per property, there's a
  `street_brawl_state_changed` event, a `CCitadelUserMsg_StreetBrawlScoring` message, an
  `EStreetBrawlRoundReset` currency source, and a dedicated **`EItemEnhancements`** currency.

**So the plan:** run the Boss Rush server on (or deriving from) the **Street Brawl ruleset** so
`AddItem` keeps granting items past 12 with no cap — exactly the "no item limit" the pitch
wants, for free, server-side. The *one* remaining gap is **visibility**: the stock HUD only
draws 12. Two options, in priority order:
1. **Ship it functional first** — items work even when hidden; perfectly playable for P1–P3.
2. **Optional custom HUD list** (client addon) later — a scrolling "owned items" panel that
   shows everything past 12. Pure presentation; doesn't change mechanics.

**Open P0 confirmation (needs a running server):** verify *how* StreetBrawl is enabled on a
Deadworks dedicated server (launch arg / map / convar / writing `m_eGameMode`) and that
`AddItem` past slot 12 then succeeds. Tracked in `docs/VERIFIED_API.md` §9.

The **Upgrade Station** (§5 / mechanic #5) is a *station interaction* (proximity zone or
`[Command]`), not a shop-catalog reskin: it charges 2× via `ModifyCurrency(EGold, −cost, …)` and
swaps the held item for its `AddItem(name, enhanced: true)` version. (Note the native
`EItemEnhancements` currency — an alternative economy worth evaluating in P2.) Reskinning the
shop's *visuals* to look like a station is an optional client Panorama job.

> See **`docs/VERIFIED_API.md`** for the exact, source-verified signatures, event types, NPC
> classnames (`npc_boss_tier1` = Guardian, `npc_boss_tier3` = Patron, `npc_trooper`), convars,
> and the open runtime experiments — the deliverable of the P0 verification pass.

---

## 4. Repository layout

```
deadlock-boss-rush/
├─ README.md                     Project overview & quick links
├─ docs/
│  ├─ DESIGN.md                  ← this file (spec + feasibility + roadmap)
│  ├─ VERIFIED_API.md            Source-verified SDK reference + open P0 experiments
│  └─ SETUP.md                   Dev environment, build, run, packaging
├─ server/                       Deadworks C# plugin (the "brain", zero client install)
│  ├─ README.md
│  └─ BossRush/
│     ├─ BossRush.csproj         net10.0 class library, refs Deadworks SDK
│     ├─ BossRushPlugin.cs       Game-mode entry + lifecycle, wires up systems
│     ├─ BossRushConfig.cs       All tunables ([PluginConfig])
│     └─ Systems/
│        ├─ SpawnDirector.cs     2× enemy troopers, doubled Guardians, denizen scaling
│        ├─ RageWaveSystem.cs    10-min 4× waves, "until cleared", audio cue
│        ├─ PatronCombatSystem.cs Patron lasers + scaling + random buffs
│        ├─ LootSystem.cs        Buddha/box pickups → AddItem, rare enhanced
│        ├─ UpgradeStation.cs    2× upgrade-to-enhanced interaction (!upgrade)
│        └─ EnhancementSystem.cs Temporary enhanced items (5 min / until death)
└─ client/                       Source 2 VPK addon source (built with CSDK 12 on Windows)
   ├─ README.md
   ├─ panorama/                  Optional custom HUD: "owned items" list (show >12 items)
   ├─ soundevents/               Rage-wave + station soundevents (.vsndevts)
   └─ vdata/                     Optional NPC/item stat overrides (KV3)
```

---

## 5. Phased roadmap

Each phase is independently demonstrable. **P0 gates everything** — until a Deadworks
server runs locally, none of the C# can be verified.

- **P0 — Verify the SDK & stand up "hello world".** *(API verification: done — see
  `docs/VERIFIED_API.md`; the scaffold is written against source-verified signatures.)* Remaining:
  build Deadworks from source; run a dedicated server; load the plugin; `connect localhost:27067`;
  confirm hot-reload; then run the live experiments in VERIFIED_API.md §9 (esp. enabling the
  Street Brawl uncapped-items ruleset, and runtime NPC spawning). *Deliverable: players join a
  server running BossRush.*
- **P1 — Core loop & spawn control.** Game-mode scaffolding, team spawn side, objective
  state machine (kill towers → Patron). `SpawnDirector`: 2× enemy troopers; stronger,
  time-scaling denizens. *Deliverable: a harder-than-normal PvE push.*
- **P2 — Economy & items.** `LootSystem` (gold-statue/crate hooks → `AddItem`, rare
  `enhanced`), `UpgradeStation` (2× → enhanced), `EnhancementSystem` (temporary enhanced items,
  5 min / until death). Confirm uncapped items under the Street Brawl ruleset (P0 experiment #1).
  *Deliverable: loot-driven progression, no shop buying.*
- **P3 — The Patron fight.** `PatronCombatSystem`: laser attacks (damage+particle+sound),
  scaling over match time, random buffs; 2× Guardians; win on Patron death.
  *Deliverable: a real boss encounter.*
- **P4 — Rage waves & audio.** `RageWaveSystem` (10-min 4× waves, until-cleared tracking);
  client addon: rage-wave soundevent + (optional) the "owned items" HUD list. *Deliverable: the
  signature hectic moment, with custom audio.*
- **P5 — Crystal buffs, tuning, polish.** Extra crystal-buff spawns (+ any map edits for
  underground/under-bridge spots), balance pass, packaging the client VPK, onboarding docs.

---

## 6. Risks & realities

- **Unstable, unofficial foundation.** Deadworks is early-dev ("APIs change without
  notice"); CSDK 12 lags game patches; `gameinfo.gi` edits reset every major Deadlock
  update. Expect maintenance churn after Deadlock patches.
- **Build-from-source only.** No prebuilt Deadworks binaries; needs VS 2026, .NET 10,
  protobuf 3.21.8, a Deadlock install (Windows).
- **Every visual/audio/map feature is a client install.** The optional owned-items HUD, rage
  audio, custom buddha model, and any map geometry all force a per-player client mod (mitigated
  by the launcher's auto-sync, but still onboarding friction).
- **No typed boss/trooper/Patron spawn helpers.** We drive behavior from low-level primitives,
  but the real classnames are now known (`npc_boss_tier3` = Patron, `npc_boss_tier1` = Guardian,
  `npc_trooper`); runtime-spawn behavior still needs a live-server check (VERIFIED_API.md §9).
- **ToS / anti-cheat is unverified.** No Valve modding policy exists. Custom servers run
  `-insecure` (outside VAC matchmaking); no bans are reported for cosmetic mods or custom
  servers, but this is community consensus, not a guarantee. **Never ship anything that
  confers an advantage in Valve matchmaking.** Confirm independently before any public
  release.

---

## 7. Open decisions (need your call before P2/P3)

1. **Items beyond 12 (§3) — resolved in principle:** use the Street Brawl uncapped-items
   ruleset (items work, hidden past 12); a custom HUD to *show* them is optional/later. Only call
   left: ship functional-but-hidden first (recommended), or invest early in the owned-items HUD?
2. **Guardian doubling.** Runtime-spawned duplicates (no client install, risk of janky
   placement) vs. a properly edited map (clean, but forces a client map download)?
3. **Upgrade Station UX.** Proximity zone + on-screen HUD prompt, or a simple interact/command
   first (faster to ship, reskin later)?
4. **Custom golden-buddha model** now (client addon work) or reuse the existing in-game gold
   statues/crates for P2 and skin later?

---

## 8. Sources

Server framework: <https://github.com/Deadworks-net/deadworks> ·
SDK: <https://github.com/Deadworks-net/deadworks/tree/main/managed/DeadworksManaged.Api> ·
examples: <https://github.com/Deadworks-net/deadworks/tree/main/examples/plugins> ·
docs: <https://docs.deadworks.net/>.
Client modding: <https://deadlockmodding.pages.dev/> · CSDK 12, addon/VPK system,
`gameinfo.gi` (<https://developer.valvesoftware.com/wiki/Gameinfo.gi/Deadlock>),
Panorama HUD, VSND/soundevents, VData, Hammer maps.
Game files (ground truth): <https://github.com/SteamTracking/GameTracking-Deadlock> ·
ValveResourceFormat / Source 2 Viewer: <https://github.com/ValveResourceFormat/ValveResourceFormat>.
