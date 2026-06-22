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
    private IHandle? _shopConvarTimer;

    public override void OnLoad(bool isReload)
    {
        _enhancements = new EnhancementSystem(Config, Timer);
        _spawns = new SpawnDirector(Config, Timer);
        _rageWaves = new RageWaveSystem(Config, Timer);
        _patron = new PatronCombatSystem(Config, Timer);
        _loot = new LootSystem(Config, _enhancements, Timer);
        _upgrades = new UpgradeStation(Config, _enhancements);
        _regen = new RegenSystem(Config, Timer);

        // Keep the (server-side) buy-enhanced convar asserted — match-init resets citadel_* convars after
        // OnStartupServer. NOTE: citadel_shop_items_appear_enhanced (the shop DISPLAY-enhanced convar) is CLIENT +
        // cheat with no archive flag — the server can't set it (engine blocks ClientCommand: "missing required
        // FCVAR flag"), Panorama has no convar API, and it doesn't persist. It can only be set client-side each
        // session (e.g. a key bind), so it's intentionally not automated here.
        _shopConvarTimer = Timer.Every(5.Seconds(), () =>
        {
            ConVar.Find("citadel_item_purchases_force_enhanced")?.SetInt(1);
        });

        Chat.PrintToChatAll(isReload
            ? "[Boss Rush] reloaded."
            : "[Boss Rush] loaded. Loot the lanes. Kill the Patron.");
        Console.WriteLine("[Boss Rush] dev commands (in-game prefix dw_): br_dumpents, br_nearby, br_pos, br_gamestate, br_spawn, br_cmds, br_run, br_ragewave, br_heal, br_additem, br_bossinfo, br_bossult, br_bossinput, br_bossfire, br_bosspromote, br_mod, br_sound, br_reloadcfg, br_allitems, br_randomitems, br_level, br_doubleguardians, br_loot, br_crates, br_enhance, br_buylegendary, br_bosscd");
    }

    public override void OnUnload()
    {
        // Cancel timers so a hot-reload doesn't leak loops.
        _shopConvarTimer?.Cancel();
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

        // EXPERIMENT (buy-enhanced pivot): make every native purchase grant the item's ENHANCED variant, and
        // make the shop DISPLAY items with enhanced data (so cards/tooltips show real enhanced stats before buying).
        // This lets the Upgrade Station sell enhanced items through the native BUY flow — souls spent and item
        // granted by the engine, no custom client→server channel needed. The client adds the native IgnoreOwnership
        // class to owned cards so clicking BUYS (enhanced) instead of selling. Reversible: drop these lines.
        ConVar.Find("citadel_item_purchases_force_enhanced")?.SetInt(1);
        ConVar.Find("citadel_shop_items_appear_enhanced")?.SetInt(1);

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
    /// CM_ClientUIEvent): clicking an OWNED item sends "sellitem &lt;upgrade_name&gt;", a buy sends
    /// "buyitem &lt;upgrade_name&gt;" — the item is the exact internal catalog name (e.g. upgrade_high_velocity_mag).
    /// We turn the SELL into an enhance: veto the native sell (HookResult.Stop) and enhance the still-held item
    /// (charge 2× + grant the enhanced variant). One click = enhance, no client channel, no UI hacks. Buys pass
    /// through (citadel_item_purchases_force_enhanced already makes them enhanced).
    /// </summary>
    public override HookResult OnClientConCommand(ClientConCommandEvent e)
    {
        if (e.Command is "sellitem" or "buyitem")
            Console.WriteLine($"[Boss Rush] shop cmd: {e.Command} [{string.Join(" ", e.Args)}]");

        // Args is argv-style: Args[0] is the command name ("sellitem"), Args[1] is the item (upgrade_<name>).
        if (e.Command == "sellitem" && e.Args.Length > 1 && e.Controller is { } caller)
        {
            _upgrades.HandleShopEnhance(caller, e.Args[1]); // item still held (we block the sell) → enhances in place
            return HookResult.Stop;                          // veto the native sell
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
