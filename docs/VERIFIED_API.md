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
