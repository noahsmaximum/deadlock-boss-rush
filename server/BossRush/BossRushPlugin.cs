using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// Boss Rush game-mode entry point. Deadworks discovers any concrete <c>DeadworksPluginBase</c>
/// in the plugins/ dir by reflection and hot-reloads it on rebuild, so this class IS the
/// game-mode registration.
///
/// Patterns here are taken from the shipped example plugins (Deathmatch, Tag, Scourge) — see
/// docs/VERIFIED_API.md for the source-verified signatures. Items beyond the 12 visible slots
/// rely on the Street Brawl ruleset (no item-slot limit); confirming how that's enabled on the
/// server is P0 experiment #1.
/// </summary>
public sealed partial class BossRushPlugin : DeadworksPluginBase
{
    public override string Name => "Boss Rush";

    /// <summary>
    /// Human heroes share one team in PvE (the Archmother's); the Hidden King's team is AI-only.
    /// Numbers per in-game observation: team 3 = Archmother (heroes), team 2 = Hidden King (enemies).
    /// </summary>
    public const int HeroTeam = 3;
    public const int EnemyTeam = 2;

    [PluginConfig]
    public BossRushConfig Config { get; set; } = new();

    // Gameplay systems — one slice of docs/DESIGN.md each.
    private EnhancementSystem _enhancements = null!;
    private SpawnDirector _spawns = null!;
    private RageWaveSystem _rageWaves = null!;
    private PatronCombatSystem _patron = null!;
    private LootSystem _loot = null!;
    private UpgradeStation _upgrades = null!;
    private RegenSystem _regen = null!;

    public override void OnLoad(bool isReload)
    {
        _enhancements = new EnhancementSystem(Config, Timer);
        _spawns = new SpawnDirector(Config, Timer);
        _rageWaves = new RageWaveSystem(Config, Timer);
        _patron = new PatronCombatSystem(Config, Timer);
        _loot = new LootSystem(Config, _enhancements, Timer);
        _upgrades = new UpgradeStation(Config, _enhancements);
        _regen = new RegenSystem(Config, Timer);

        Chat.PrintToChatAll(isReload
            ? "[Boss Rush] reloaded."
            : "[Boss Rush] loaded. Loot the lanes. Kill the Patron.");
        Console.WriteLine("[Boss Rush] dev commands (in-game prefix dw_): br_dumpents, br_nearby, br_pos, br_gamestate, br_spawn, br_cmds, br_run, br_ragewave, br_heal, br_additem, br_bossinfo, br_bossult, br_bossinput, br_bossfire, br_bosspromote, br_mod, br_sound, br_reloadcfg, br_allitems, br_randomitems, br_level, br_doubleguardians, br_loot, br_crates, br_enhance, br_buylegendary, br_bosscd");
    }

    public override void OnUnload()
    {
        // Cancel timers so a hot-reload doesn't leak loops.
        _rageWaves.Stop();
        _spawns.Stop();
        _patron.Stop();
        _regen.Stop();
        _enhancements.Clear();
    }

    /// <summary>Fires on a fresh map — set the ruleset and arm match-scoped systems.</summary>
    public override void OnStartupServer()
    {
        ApplyRuleset();
        _spawns.Start(); // scale the Hidden King's lane troopers up over time
        _patron.Start();  // Patron attack + buff loops
        _rageWaves.Start(); // periodic lane floods
        _regen.Start(); // slow hero-only health regen

        // NOTE: automatic team-forcing is disabled — repeatedly calling ChangeTeam during hero
        // select broke spawning (stuck portrait, no hero load). Use dw_br_forceteam to move
        // players manually until a non-disruptive assignment mechanism is found.
    }

    /// <summary>
    /// Server convars that shape the mode. Names are confirmed from the example plugins; tune
    /// during P1. TODO(P0): set/confirm the Street Brawl game mode here so items are uncapped.
    /// </summary>
    private void ApplyRuleset()
    {
        // citadel_spawn_trooper(_grid), used by the wave systems, is cheat-gated.
        ConVar.Find("sv_cheats")?.SetInt(1);
        // Heroes' own troopers spawn normally; SpawnDirector adds the enemy 2× on top.
        ConVar.Find("citadel_trooper_spawn_enabled")?.SetInt(1);
        ConVar.Find("citadel_npc_spawn_enabled")?.SetInt(1);
        // Buying happens at Upgrade Stations, not anywhere — but allow for now while prototyping.
        ConVar.Find("citadel_allow_purchasing_anywhere")?.SetInt(1);
        ConVar.Find("citadel_allow_duplicate_heroes")?.SetInt(1);

        // Item-slot capacity: unlock the flex slots so players can hold more bought items before the native
        // "replace an item?" prompt. The true no-limit is the Street Brawl ruleset, but enabling it crashes our
        // Invalid-mode PvE match — so flex unlock is the safe lever. (Granting via AddItem still bypasses slots;
        // it's the SHOP BUY path that enforces them client-side.)
        ConVar.Find("citadel_hero_demo_unlock_flex_slots")?.SetInt(1);
        Server.ExecuteCommand("citadel_unlock_flex_slots"); // no team arg = both teams

        // NOTE: we intentionally do NOT set citadel_item_purchases_force_enhanced — it auto-enhanced every native
        // purchase (e.g. imbue items bought through their own popup came out enhanced at base price, bypassing our
        // intercept). The Upgrade Station grants enhanced variants explicitly via the sellitem→enhance path instead.
        // citadel_shop_items_appear_enhanced is also dropped (it's a cl+cheat convar the server can't set anyway).

        // NOTE: the 23 "legendaries" are Street Brawl items (m_eAbilityRequirements="ERequirementStreetBrawl").
        // Enabling citadel_gamemode_streetbrawl_enabled to satisfy that requirement CRASHES the server (the mode
        // can't run on our Invalid-mode PvE match). Instead we unlock their purchase client-side by clearing that
        // requirement in our abilities.vdata override (no mode change) — the client then sends buyitem and our
        // OnClientConCommand intercept grants + charges the flat legendary price.

        // Front-load loot crates: spawn them at match start instead of after the default delay (the bridge-buff
        // powerup spawners are a separate system and untouched). Convar names confirmed in server.dll.
        if (Config.FrontloadCrates)
        {
            ConVar.Find("citadel_crate_spawn_enabled")?.SetInt(1);
            ConVar.Find("citadel_crate_spawn_initial_delay")?.SetInt(0);
            ConVar.Find("citadel_crate_early_to_trooper_spawn_delay")?.SetInt(0);
            // Force the world breakables/props out from t=0 as well.
            ConVar.Find("citadel_breakable_prop_initial_spawn_time_override")?.SetInt(0);
            if (Config.CrateRespawnIntervalSeconds >= 0)
                ConVar.Find("citadel_crate_respawn_interval")?.SetInt(Config.CrateRespawnIntervalSeconds);
            // Repopulate the world breakables (the bulk loot source) faster so the mid-game isn't a drought.
            if (Config.BreakableRespawnSeconds >= 0)
                ConVar.Find("citadel_breakable_prop_spawn_interval_override")?.SetInt(Config.BreakableRespawnSeconds);
        }
        // Hero health regen is handled by RegenSystem (custom rate) — sv_regeneration_force_on has
        // no rate cvar in Deadlock, so it can't be slowed.

        // TODO(P0): enable StreetBrawl ruleset (uncapped items). Mechanism TBD on a live server
        // (launch arg / map / convar / writing GameRules m_eGameMode) — see VERIFIED_API.md §9.
    }

    /// <summary>Pause a hero's regen when they take damage.</summary>
    public override HookResult OnTakeDamage(TakeDamageEvent args)
    {
        if (args.Entity.As<CCitadelPlayerPawn>() is { } pawn)
            _regen.OnHeroDamaged(pawn);
        return HookResult.Continue;
    }

    /// <summary>
    /// Upgrade Station — the real intercept. The client relays shop clicks to the server as concommands (over
    /// CM_ClientUIEvent): clicking an OWNED item sends "sellitem &lt;upgrade_name&gt;", clicking a BUYABLE item sends
    /// "buyitem &lt;upgrade_name&gt;" — the item is the exact internal catalog name (e.g. upgrade_high_velocity_mag).
    /// Both are vetoable concommands, so we redirect them to the station:
    ///   • SELL (owned card) → enhance the still-held item (charge 2× + grant the enhanced variant).
    ///   • BUY (legendary card) → grant the legendary at the flat <see cref="BossRushConfig.LegendaryPrice"/>.
    ///   • BUY (anything else) → blocked when <see cref="BossRushConfig.StoreLegendariesOnly"/> (power = world loot).
    /// One click each, no client channel, no UI hacks. Args is argv-style: Args[0] = command name, Args[1] = item.
    /// </summary>
    public override HookResult OnClientConCommand(ClientConCommandEvent e)
    {
        if (e.Command is "sellitem" or "buyitem" or "buydependentitem")
            Console.WriteLine($"[Boss Rush] shop cmd: {e.Command} [{string.Join(" ", e.Args)}]");

        if (e.Controller is not { } caller || e.Args.Length <= 1)
            return HookResult.Continue;
        var item = e.Args[1];

        if (e.Command == "sellitem")
        {
            _upgrades.HandleShopEnhance(caller, item); // item still held (we block the sell) → enhances in place
            return HookResult.Stop;                     // veto the native sell
        }

        if (e.Command == "buyitem")
        {
            if (ItemCatalog.TierOf(item) == Config.LegendaryTier)
            {
                _upgrades.HandleBuyLegendaryCommand(caller, item); // flat-price grant (veto native — native price is wrong)
                return HookResult.Stop;
            }
            if (Config.StoreLegendariesOnly)
            {
                Chat.PrintToChat(caller, "[Boss Rush] base items are loot-only — only legendaries are sold at the Mythic Altar.");
                return HookResult.Stop; // block base purchases
            }
        }

        // Imbue legendaries buy through their own popup as "buydependentitem <item> <abilitySlot>". Vetoing would
        // skip the imbue, so we let the native buy+imbue run and then force the NET cost to the flat legendary price
        // on the next tick — independent of whatever the engine charged for the relabeled tier-4 card.
        if (e.Command == "buydependentitem" && ItemCatalog.TierOf(item) == Config.LegendaryTier
            && caller.GetHeroPawn()?.As<CCitadelPlayerPawn>() is { } imbuePawn)
        {
            int before = imbuePawn.GetCurrency(ECurrencyType.EGold);
            if (before < Config.LegendaryPrice)
            {
                Chat.PrintToChat(caller, $"[Boss Rush] need {Config.LegendaryPrice} souls for {item}.");
                return HookResult.Stop; // can't afford the flat legendary price — block before the imbue runs
            }
            int target = before - Config.LegendaryPrice;
            Timer.Once(0.2.Seconds(), () =>
            {
                if (caller.GetHeroPawn()?.As<CCitadelPlayerPawn>() is { } p)
                {
                    int now = p.GetCurrency(ECurrencyType.EGold);
                    if (now > target) // deduct only the surcharge beyond what the engine already took
                        p.ModifyCurrency(ECurrencyType.EGold, -(now - target), ECurrencySource.ECheats, spendOnly: true);
                }
            });
            return HookResult.Continue; // let native do the imbue + its own charge
        }
        return HookResult.Continue;
    }

    // NOTE (verified in-game 2026-06-20): blocking native item buys via OnModifyCurrency returning Stop on
    // EItemPurchase spends does NOT work — the hook fires, but souls are still spent and the item is still granted.
    // The currency hook is observe-only for purchases; there is no server veto here. Buy-gating is therefore done
    // CLIENT-side in the shop UI (hide un-owned items so there's no buy option; owned items show Enhance; the
    // legendary tab lists buyable legendaries). See the client addon's citadel_hud_hero_shop reskin.

    public override void OnPrecacheResources()
    {
        // Boss-ult VFX (real shipped tier3boss/patron particles). Precache runs on MAP LOAD — a plugin
        // hot-reload does NOT re-run it, so newly added particles only register after a map change/restart.
        Precache.AddResource(Config.PatronLaserParticle);
        Precache.AddResource(Config.BossExplodeParticle);
        Precache.AddResource(Config.BossBarrageParticle);
        Precache.AddResource(Config.BossStormCloudParticle);
        Precache.AddResource(Config.BossStormBoltParticle);
        Precache.AddResource(Config.BossStormStrikeParticle);
        Precache.AddResource(Config.BossStormZapParticle);
        Precache.AddResource(Config.BossChargeChargeParticle);
        Precache.AddResource(Config.BossChargeGroundParticle);
        Precache.AddResource(Config.BossChargeExplodeParticle);
        Precache.AddResource(Config.BossChargeWaveParticle);
        // NOTE: Precache.AddHero(Heroes.Familiar) does NOT register modifier_familiar_asleep's VData
        // (tested live) — the boss's real sleep needs that modifier shipped in a server-loaded VPK (P4).
        // TODO(P3/P4): precache rage-wave / station particles + custom sounds shipped in client addon.
    }

    // ── Event hooks ───────────────────────────────────────────────────────────────

    [GameEventHandler("player_death")]
    public HookResult OnPlayerDeath(PlayerDeathEvent args)
    {
        // Timed enhancements end on death (DESIGN.md #8). UseridPawn is the base pawn type;
        // convert to the Citadel pawn the way the example plugins do.
        if (args.UseridPawn?.As<CCitadelPlayerPawn>() is { } victim)
            _enhancements.OnPlayerDeath(victim);
        return HookResult.Continue;
    }

    /// <summary>World-loot pickups + Upgrade-Station zones are entity touches (returns void).</summary>
    public override void OnEntityStartTouch(EntityTouchEvent args)
    {
        _loot.OnEntityTouch(args);
        _upgrades.OnEntityTouch(args);
    }

    /// <summary>Scale denizens up as they spawn; capture the Patron for the Hidden King fight.</summary>
    public override void OnEntitySpawned(EntitySpawnedEvent args)
    {
        _spawns.OnEntitySpawned(args);
        _patron.OnEntitySpawned(args);
        _loot.OnEntitySpawned(args);
    }

    /// <summary>Upgrade Station — command-driven until the client station UI lands (P4).</summary>
    [Command("br_enhance", Description = "Enhance a held item at 2× its tier price (Upgrade Station)")]
    public void CmdEnhance(CCitadelPlayerController caller, string itemName)
    {
        _upgrades.HandleEnhanceCommand(caller, itemName);
    }

    [Command("br_buylegendary", Description = "Buy a legendary (T5) item for a flat price (Upgrade Station)")]
    public void CmdBuyLegendary(CCitadelPlayerController caller, string itemName)
    {
        _upgrades.HandleBuyLegendaryCommand(caller, itemName);
    }
}
