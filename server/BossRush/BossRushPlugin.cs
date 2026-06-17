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

    public override void OnLoad(bool isReload)
    {
        _enhancements = new EnhancementSystem(Config, Timer);
        _spawns = new SpawnDirector(Config, Timer);
        _rageWaves = new RageWaveSystem(Config, Timer);
        _patron = new PatronCombatSystem(Config, Timer);
        _loot = new LootSystem(Config, _enhancements);
        _upgrades = new UpgradeStation(Config, _enhancements);

        Chat.PrintToChatAll(isReload
            ? "[Boss Rush] reloaded."
            : "[Boss Rush] loaded. Loot the lanes. Kill the Patron.");
        Console.WriteLine("[Boss Rush] dev commands: br_dumpents, br_nearby, br_pos, br_gamestate, br_spawn, br_cmds, br_run, br_ragewave, br_forceteam");
    }

    public override void OnUnload()
    {
        // Cancel timers so a hot-reload doesn't leak loops.
        _rageWaves.Stop();
        _spawns.Stop();
        _patron.Stop();
        _enhancements.Clear();
    }

    /// <summary>Fires on a fresh map — set the ruleset and arm match-scoped systems.</summary>
    public override void OnStartupServer()
    {
        ApplyRuleset();
        _spawns.Start(); // scale the Hidden King's lane troopers up over time
        _patron.Start();  // Patron attack + buff loops
        _rageWaves.Start(); // periodic lane floods

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
        // TODO(P0): enable StreetBrawl ruleset (uncapped items). Mechanism TBD on a live server
        // (launch arg / map / convar / writing GameRules m_eGameMode) — see VERIFIED_API.md §9.
    }

    public override void OnPrecacheResources()
    {
        Precache.AddResource(Config.PatronLaserParticle);
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

    /// <summary>Scale denizens up as they spawn; route loot containers if spawned dynamically.</summary>
    public override void OnEntitySpawned(EntitySpawnedEvent args)
    {
        _spawns.OnEntitySpawned(args);
    }

    /// <summary>`!upgrade <item>` until the Upgrade-Station zone UX lands (P2).</summary>
    [Command("upgrade", Description = "Enhance a held item at 2× its price (Upgrade Station)")]
    public void CmdUpgrade(CCitadelPlayerController caller, string itemName)
    {
        _upgrades.HandleUpgradeCommand(caller, itemName);
    }
}
