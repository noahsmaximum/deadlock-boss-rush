# Verified Deadworks API reference (P0)

Pinned from reading the **actual Deadworks source** (`Deadworks-net/deadworks`, cloned and
read directly — `managed/DeadworksManaged.Api` + `examples/plugins`), not from web summaries.
This is the ground truth the `server/` scaffold is written against. Anything still requiring a
running server to confirm is in **§9 Open P0 experiments**.

> The framework is still "early development; APIs change without notice." Re-verify against
> your own clone before a release; signatures here are accurate as of the clone in this branch.

---

## 1. Plugin shape
```csharp
public sealed class BossRushPlugin : DeadworksPluginBase {
    public override string Name => "Boss Rush";
    [PluginConfig] public BossRushConfig Config { get; set; } = new();
    public override void OnLoad(bool isReload) { }
    public override void OnUnload() { }
    protected ITimer Timer { get; } // inherited
}
```
Discovered by reflection in the server's `plugins/` dir; **hot-reloads** on DLL change.

**Virtual lifecycle/hooks** (override as needed): `OnPrecacheResources()`,
`OnStartupServer()`, `OnGameFrame(bool simulating, bool firstTick, bool lastTick)`,
`OnTakeDamage(TakeDamageEvent)→HookResult`, `OnModifyCurrency(ModifyCurrencyEvent)→HookResult`,
`OnChatMessage(ChatMessage)→HookResult`, `OnClientConCommand(ClientConCommandEvent)→HookResult`,
`OnClientConnect(ClientConnectEvent)→bool`, `OnClientPutInServer/FullConnect/Disconnect(...)`,
`OnEntityCreated/Spawned/Deleted(...)`, **`OnEntityStartTouch/EndTouch(EntityTouchEvent)` → `void`**
(⚠️ not HookResult), `OnAbilityAttempt(AbilityAttemptEvent)`, `OnAddModifier(AddModifierEvent)→HookResult`,
`OnConfigReloaded()`.

**Attributes:** `[PluginConfig]`; `[GameEventHandler("name")]` (typed event arg, returns
`HookResult`); `[Command("name", Description="…")]` (first param `CCitadelPlayerController caller`,
optional extra args); `[NetMessageHandler]` (`OutgoingMessageContext<T>`); `[ConVar]`.

**Named game events → typed args** (confirmed): `player_death`→`PlayerDeathEvent`
(`.AttackerPawn`, `.UseridPawn`, `.AttackerController`, `.Userid`), `player_respawned`→
`PlayerRespawnedEvent` (`.Userid`), `player_hero_changed`→`PlayerHeroChangedEvent`. Raw event
names also exist for `street_brawl_state_changed`, `inventory_updated`.

## 2. Player (`CCitadelPlayerPawn : CBasePlayerPawn`)
- `AddItem(string itemName, bool enhanced=false) → CBaseEntity?` — `enhanced:true` gives the
  native **enhanced** variant. Returns null on failure.
- `RemoveItem(string) → bool` (no refund); `SellItem(string, bool fullRefund=false, bool forceSellPrice=false) → bool` (refunds).
- `AddAbility(string, ushort slot)`, `RemoveAbility(...)`, `GetAbilityBySlot(EAbilitySlot)`, `ExecuteAbilityBySlot/ByID(...)`.
- `Level {get;set}`, `GetCurrency(ECurrencyType)`, `SetCurrency(...)`,
  `ModifyCurrency(ECurrencyType type, int amount, ECurrencySource source, bool silent=false, bool forceGain=false, bool spendOnly=false)` — negative `amount` spends.
- `ResetHero(bool resetAbilities=true)`, `SwapOrReset(Heroes, Action? onReady=null)`, `OnceHeroInitialized(Action)`.
- `Position`, `Teleport(position:, angles:)`, `TeamNum`, `Health`, `GetMaxHealth()`, `Heal(float)`,
  `Controller`, `PlayerData/PlayerDataGlobal` (`.HealthMax`, `.HeroID`), `HeroID`, `EntityIndex`,
  `EntityHandle`, `AbilityComponent`, `EyePosition`, `ViewAngles`.

Controller `CCitadelPlayerController`: `GetHeroPawn()`, `ChangeTeam(int)`, `SelectHero(Heroes)`,
`PlayerName`, `PlayerDataGlobal.HeroID`, `EntityIndex`, `Remove()`.
`Players.GetAll()` → controllers; `Players.GetAllPawns()` → pawns.

## 3. Entities (`CBaseEntity`)
- static `CreateByName(string className) → CBaseEntity?`, `CreateByDesignerName(string) → CBaseEntity?`,
  `FromHandle(uint) → CBaseEntity?`, `FromIndex<T>(int)`.
- `Spawn()`, `Spawn(CEntityKeyValues ekv)`; `Position`, `Teleport(Vector3? position=null, Vector3? angles=null)`,
  `SetModel`, `SetScale`, `SetParent/ClearParent`, `TeamNum`, `Health`, `GetMaxHealth()`, `IsAlive`,
  `DesignerName`, `EntityIndex`, `EntityHandle`, `Remove()`, `As<T>()`, `AcceptInput(string input, …)`.
- `AddModifier(string name, KeyValues3? kv=null, CBaseEntity? caster=null, CBaseEntity? ability=null, int team=0) → CBaseModifier?`
  (also a `Dictionary<string,float>` overload); `RemoveModifier(...)`.
- `Hurt(float damage, CBaseEntity? attacker=null, CBaseEntity? inflictor=null, CBaseEntity? ability=null, int damageType=0)`; `TakeDamage(CTakeDamageInfo)`.
- `EmitSound(string soundName, int pitch=100, float volume=1.0f, float delay=0.0f)`.

## 4. VFX / world text
- `CParticleSystem.Create(effectName).AtPosition(v).WithControlPoint(index, ent).WithDataCP(cp, v).WithTint(color).StartActive(true).Spawn() → CParticleSystem?`
  then `.Start()/.Stop()/.Destroy()/.AttachTo(ent)`. For a **beam/laser**: CP0 = origin (position), CP1 = target via `WithControlPoint(1, targetEntity)`.
- `CPointWorldText.Create(text, Vector3 pos, fontSize, r, g, b, fontName)` → in-world 3D text
  (`.Teleport(angles:)`, `.WorldUnitsPerPx`, `.JustifyHorizontal/Vertical`). Good for labeling stations/objectives.

## 5. Timers (`ITimer` via `Timer`)
`Once(Duration, Action)→IHandle`, `Every(Duration, Action)→IHandle`,
`Sequence(Func<IStep,Pace>)→IHandle`, `NextTick(Action)`. Duration helpers: `5.Minutes()`,
`30.Seconds()`, `200.Milliseconds()`. `IHandle.Cancel()`. In a sequence: `step.Run` (iteration
count), `step.Wait(Duration)`, `step.Done()`.

**DoT / repeated-attack pattern** (ScourgePlugin — this is the Patron-laser template):
```csharp
Timer.Sequence(step => {
    if (step.Run > maxTicks) return step.Done();
    var ent = CBaseEntity.FromHandle(victimHandle);
    if (ent == null || !ent.IsAlive) return step.Done();
    ent.Hurt(dmg, attacker: attacker);
    ent.EmitSound(sound, volume: 0.1f);
    return step.Wait(intervalMs.Milliseconds());
});
```

## 6. Messaging / UI / chat
- `NetMessages.Send(msg, RecipientFilter.All | RecipientFilter.Single(slot))`.
- `new CCitadelUserMsg_HudGameAnnouncement { TitleLocstring = "...", DescriptionLocstring = "..." }` — big center announcement.
- `new CCitadelUserMsg_ChatMsg { PlayerSlot = -1, Text = "...", AllChat = true }`.
- `Chat.PrintToChat(int slot, string)`, `Chat.PrintToChat(CCitadelPlayerController, string)`, **`Chat.PrintToChatAll(string)`** (⚠️ there is no null-caller overload).

## 7. Server config / convars
`ConVar.Find("name")?.SetInt(n) / .SetFloat(f)`; `Server.ExecuteCommand("...")`; `Server.MapName`.
Convars seen in the official examples (relevant to us):
`citadel_trooper_spawn_enabled`, `citadel_npc_spawn_enabled`, `citadel_active_lane`,
`citadel_player_starting_gold`, `citadel_allow_purchasing_anywhere`, `citadel_item_sell_price_ratio`,
`citadel_allow_duplicate_heroes`, `citadel_player_spawn_time_max_respawn_time`,
`citadel_rapid_stamina_regen`, `citadel_start_players_on_zipline`, `citadel_voice_all_talk`;
command `citadel_unlock_flex_slots`.

`GameRules` (static): **`GameClock`** (seconds since match start, pause-aware — use for time
scaling), `GameMode → ECitadelGameMode`, `GameState`, `MidbossKillCount`, `AmberRejuvCount`,
`SapphireRejuvCount`, `NextMidBossSpawnTime`, `IsValid`.

`EntityData<T>` — per-entity/per-player state store: `data[pawn] = v; data.TryGet(pawn, out var v); data.Has(c); data.Remove(c); data.Clear();`
(use this for per-player relics/enhancements instead of hashing).
`Precache.AddHero(Heroes.X)` / `Precache.AddResource("particles/….vpcf")` in `OnPrecacheResources()`.
`KeyValues3`: `using var kv = new KeyValues3(); kv.SetFloat("duration", 3f);`.

## 8. Enums & known names
- **`ECurrencyType`**: `EGold=0` (souls), `EAbilityPoints=1`, `EAbilityUnlocks=2`,
  `EDeathPenaltyGold=3`, `EItemDraftRerolls=4`, **`EItemEnhancements=5`** (a dedicated
  enhancement currency exists!).
- **`ECurrencySource`**: `ECheats`, `EStartingAmount`, `EItemPurchase`, `EItemSale`,
  `EStreetBrawlRoundReset=0x2b`, …
- **`ECitadelGameMode`**: `Invalid=0, Normal=1, OneVOneTest=2, Sandbox=3, StreetBrawl=4, ExploreNYC=5, Internal=6`.
- Others used: `Heroes` (e.g. `Warden`, `Astro`), `EAbilitySlot` (`Signature1..4`),
  `TakeDamageFlags` (`LightMelee`, `HeavyMelee`), `InputButton` (`AllAbilities`, `AllItems`),
  `HookResult` (`Continue`, `Stop`).
- **NPC / objective designer names** (real, from examples):
  `npc_trooper` (lane trooper), `npc_trooper_boss` (super/siege trooper),
  **`npc_boss_tier1` = Guardian**, **`npc_boss_tier2` = Walker**, **`npc_boss_tier3` = Patron**,
  `npc_barrack_boss`, `npc_base_defense_sentry`. Gamerules proxy: `citadel_gamerules`.
- Example real item id: `upgrade_sprint_booster`, `upgrade_discord`.

## ★ Street Brawl = the key to "unlimited items"
The pitch's "no item limit" is a **real, supported** capability, not a wall:
- `ECitadelGameMode.StreetBrawl` is a first-class mode; `GameRules.GameMode` reads it live.
- Items carry a **separate `m_strStreetBrawlValue` per property** (the C++ SDK reads it), there's
  a `street_brawl_state_changed` event, `CCitadelUserMsg_StreetBrawlScoring`, and
  `ECurrencySource.EStreetBrawlRoundReset` — Street Brawl is a deeply-supported ruleset.
- Since the **Mar 6 2026 patch**, Street Brawl has **no item-slot limit** — items past the 12
  visible stay equipped and functional (just hidden in the HUD). So a Boss Rush server based on
  the Street Brawl ruleset inherits unlimited items for free; the only gap is *showing* >12 (a
  custom HUD list, optional).

## 9. Open P0 experiments (need a running server — do these on the Windows box)

> **Tooling:** the plugin ships dev commands to run these fast — `br_dumpents` (dump every
> entity's designer name → answers the spawn-classname questions), `br_nearby`, `br_pos`
> (record coordinates), `br_gamestate` (confirms the active game mode). See `Systems/DevTools.cs`.
>
> **Sig fix (2026-06-16):** the May-22 `CCitadelPlayerPawn::AbilityThink` pattern
> (`48 89 4C 24 ?? 55 53 56 41 55 41 57`) no longer matches the **June-11 Deadlock build**.
> Replace it in `<Deadlock>\game\citadel\cfg\deadworks_mem.jsonc` (and source
> `config/deadworks_mem.jsonc`) with the community PR pattern
> `40 55 53 41 54 41 55 41 57 48 8D AC 24`. Deadworks then hooks AbilityThink and boots .NET.
>
> **Deploy fix (2026-06-16):** after the sig fix, `deadworks.exe` crashes at map load with an
> unhandled CLR `System.IO.FileNotFoundException` for `Google.Protobuf, Version=3.29.3.0`
> (minidump exception code `0xE0434352` = managed exception, raised in KERNELBASE; not a native
> AV). Cause: `DeployToGame` in `managed/DeadworksManaged.csproj` copies the managed DLLs but
> **omits their `Google.Protobuf` dependency**, so the protobuf game-session-manifest build has
> no `Google.Protobuf.dll` to load. Fix: copy `Google.Protobuf.dll` (3.29.3, net8.0) into
> `<Deadlock>\game\bin\win64\managed\`, or add `$(OutputPath)Google.Protobuf.dll` to the
> `DeployFiles` list and rebuild. The crash happens before any plugin `OnLoad` — it is a
> Deadworks deployment gap, not plugin code.
1. **Enable unlimited items:** confirm how StreetBrawl is set on a Deadworks dedicated server
   (launch arg / map / convar / writing `m_eGameMode`), and that `AddItem` past slot 12 then
   succeeds. (Fallback: investigate any item-cap convar.)
2. **Spawning NPCs:** confirm `CreateByName` vs `CreateByDesignerName` for `npc_trooper` /
   `npc_boss_tier1`, and that a spawned trooper can be team-assigned (`TeamNum`) and will path a
   lane. (Map objectives may need to exist; troopers may require a spawner.)
3. **Coordinates:** record lane/station/buff spawn points in-game with an `addspawn`-style
   `[Command]` (see TagPlugin) → bake into config.
4. **Patron combat:** resolve the live `npc_boss_tier3` entity, confirm `Hurt` on a hero from it
   registers, and pick a real beam `.vpcf` path for the laser.
5. **Crystal/rejuv pickups:** find the rejuvenator/crystal entity classname and whether it spawns
   at arbitrary coords (relates to `AmberRejuvCount`/`SapphireRejuvCount`).
6. **Hero-ult casting on the boss (the Hidden King — DESIGN.md §4):** can a non-hero pawn
   (`npc_boss_tier3`) fire a hero ultimate? Try `AddAbility("ability_…", slot)` + `ExecuteAbilityByID`,
   vs spawning the ability child-ent and triggering it, vs `AcceptInput`. Record which ults resolve
   and their real ability ids. Also confirm reading `m_ePhase`/`m_eAliveState` transitions from
   managed code (hook vs. poll) and whether phases can be *added* beyond the native count.
7. **Legendary-only shop at a steep price (mechanic #14):** confirm how to (a) restrict purchasing
   to legendary-tier items and (b) inflate their cost — intercepting `OnModifyCurrency`
   (source `EItemPurchase`) to reject/scale, vs item-cost cvars. Note the interaction with
   `citadel_allow_purchasing_anywhere`.

---

## 10. Live entity census — `dl_midtown`, idle server (2026-06-16)

Captured with `dw_br_dumpents` on a dedicated server at `state=Init` (no players, no match):
**3431 entities, 99 distinct designer names.** Top names mapped to Boss Rush systems:

| Designer name | Count | Use for Boss Rush |
|---|---|---|
| `citadel_breakable_prop` | 691 | breakable loot urns/crates already in the map → LootSystem hook target |
| `info_neutral_trooper_spawn` | 228 | neutral/jungle camp spawn points |
| `info_neutral_trooper_camp` | 51 | neutral camp anchors |
| `npc_neutral_bug` | 69 | live neutral creeps (already spawned at idle) |
| `info_trooper_spawn` | 24 | **lane trooper spawn points** |
| `info_super_trooper_spawn` | 12 | super-trooper spawn points |
| `npc_barrack_boss` | 12 | **Guardian / lane-boss NPC** (candidate mini-boss) |
| `info_team_spawn` | 89 | team spawn points |
| `trigger_item_shop` | 9 | in-map shops → Upgrade Station siting |
| `lane_marker_path` | 12 | lane geometry/path nodes |

**Key facts (idle):**
- `GameRules`: `mode=Invalid`, `state=Init` — the bare `+map dl_midtown` launch does **not** set a
  real game mode; no Street Brawl ruleset is active until a match is configured / players join.
- **Dynamic NPCs are absent at idle:** no `npc_trooper`, no `npc_boss_tier3` (Patron). Lane
  troopers and the mid-boss only spawn once a match is in progress — capture those with a second
  `dw_br_dumpents` *after* a client connects and the match starts.
- Full per-entity positions (spawn points, shops, lanes) live in the JSON dump
  (`~\deadlock_dumps\entdump_*.json`) for baking into config.

---

## 11. Runtime spawn results — `CreateByDesignerName` + `Spawn()` (2026-06-16, live)

Tested with `dw_br_spawn` against a connected hero. Spawn path:
`CBaseEntity.CreateByDesignerName(name)` → set `TeamNum` → `Teleport(pos)` → `Spawn()`.

| Designer name | Result |
|---|---|
| `npc_boss_tier3` | ✅ **= the Patron.** Spawns, attacks, and **killing it wins the match** (native win condition). Brings child ability ents `citadel_ability_tier3boss_laser_beam` + `citadel_ability_tier3boss_aoe_wave` → no custom laser particle needed. |
| `npc_barrack_boss` | ✅ Guardian (`CNPC_BarrackBoss`). Spawns clean. |
| `npc_neutral_bug` | ✅ Spawns (`CNPC_Neutral_Bug`) but at **hp 1/1** — set `MaxHealth`/`Health` after spawn to make it a threat. |
| `npc_trooper` | ❌ **Crashes the server** — `0xC0000005` AV inside native `CBaseEntity.Spawn()`. Lane troopers need the game's lane/spawner context (null lane deref). A native AV can't be caught in managed → process dies. Do **not** raw-spawn; `br_spawn` now refuses `npc_trooper`/`npc_super_trooper`. |

**Implications for the mode:**
- Build waves from entities that spawn cleanly (bosses / `npc_barrack_boss` / neutrals), not raw
  lane troopers. Fits a "Boss Rush" better anyway.
- `npc_boss_tier3` is the finale: spawning + killing it is the win, for free.
- Set health on spawned enemies (neutrals are 1 hp).
- `EGameState` values observed: 2=WaitingForPlayersToJoin, 3=HeroSelection, 4=MatchIntro,
  5=WaitForMapToLoad, 6=PreGameWait, 7=GameInProgress. `CNPC_Boss_Tier3` has `m_ePhase`
  (ETier3Phase_t) + `m_eAliveState` (ETier3State_t) — the engine hook for the multi-phase
  Hidden King (DESIGN.md §4).
- **Still open:** spawning real lane troopers safely (needs the game's trooper spawner, not
  `CreateByDesignerName`); the `npc_barrack_boss`/boss tier1/tier2 ladder; tuning health/teams.

---

## 12. Native lane-trooper spawning & control (found via `dw_br_cmds`, 2026-06-16)

The marching **wave troopers** (which fight) — not the idle denizens — are spawned by the game's
own console commands, which do the lane/spawner setup that `CreateByDesignerName()+Spawn()` skips
(that path AVs on `npc_trooper`):

- `citadel_spawn_trooper` — "Creates a new trooper NPC and spawn them in front of the player"
- `citadel_spawn_trooper_grid` — "NxN trooper grid in front of the player"
- `citadel_spawn_trooper_zipline` — "Spawn a trooper on a zipline"
- `trooper_kill_all`, `trooper_kill_non_bosses`, `citadel_destroy_all_npcs` — cleanup

These are cheat-gated, so a player can't run them directly. Drive them from the plugin:
`ConVar.Find("sv_cheats")?.SetInt(1)` then `Server.ClientCommand(caller.Slot, "citadel_spawn_trooper")`
— runs in the player's context so "in front of the player" resolves. `br_run` does exactly this.

**Wave-tuning cvars** (the game already spawns lane waves — crank these for Boss Rush difficulty
instead of spawning each trooper ourselves):
- `citadel_trooper_spawn_enabled` (master toggle), `citadel_trooper_squad_size` (4/wave)
- `citadel_trooper_spawn_interval_early/late/very_late` (30/25/20s), `_initial` (16s), `_spawn_wave_spread`
- `citadel_trooper_health_mult` (1.5), `citadel_trooper_health_mult_gametime` (35)
- `citadel_trooper_max_per_lane` (0 = uncapped), `citadel_trooper_use_ziplines`, `citadel_trooper_shooting_enabled`

**Note:** `npc_create` / `npc_create_aimed` exist but spawn idle denizen/neutral NPCs that don't
fight back — wrong for wave enemies.
