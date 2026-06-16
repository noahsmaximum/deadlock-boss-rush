using DeadworksManaged.Api; // ⚠️ provisional namespace — verify against your SDK clone (P0)

namespace BossRush;

/// <summary>
/// Boss Rush game-mode entry point. Deadworks discovers any concrete <c>DeadworksPluginBase</c>
/// in the plugins/ dir by reflection and hot-reloads it on rebuild, so this class IS the
/// game-mode registration — there's no separate "register mode" call.
///
/// Lifecycle &amp; hook patterns below mirror the shipped example plugins (Deathmatch, Scourge,
/// RollTheDice). Method signatures are PROVISIONAL until pinned against the SDK in roadmap P0.
/// See docs/DESIGN.md.
/// </summary>
public sealed class BossRushPlugin : DeadworksPluginBase
{
    public override string Name => "Boss Rush";

    [PluginConfig]
    public BossRushConfig Config { get; set; } = new();

    // Gameplay systems. Each owns one slice of DESIGN.md so files stay focused.
    private SpawnDirector _spawns = null!;
    private RageWaveSystem _rageWaves = null!;
    private PatronCombatSystem _patron = null!;
    private LootSystem _loot = null!;
    private UpgradeStation _upgrades = null!;
    private RelicSystem _relics = null!;

    public override void OnLoad(bool isReload)
    {
        // `Timer` is provided by DeadworksPluginBase; pass it (and config) into the systems.
        _relics = new RelicSystem(Config, Timer);
        _spawns = new SpawnDirector(Config, Timer);
        _rageWaves = new RageWaveSystem(Config, Timer);
        _patron = new PatronCombatSystem(Config, Timer);
        _loot = new LootSystem(Config, _relics);
        _upgrades = new UpgradeStation(Config);

        // Long-running loops. (Spawn director and patron combat arm themselves when the
        // match actually starts — see OnStartupServer — so a hot-reload mid-match is clean.)
        _rageWaves.Start();

        Chat.PrintToChat(null, isReload
            ? "[Boss Rush] plugin reloaded."
            : "[Boss Rush] loaded. Survive the lanes. Kill the Patron.");
    }

    public override void OnUnload()
    {
        // Cancel timers / detach anything stateful so hot-reload doesn't leak loops.
        _rageWaves.Stop();
        _spawns.Stop();
        _patron.Stop();
    }

    /// <summary>Fires on a fresh map — good place to (re)arm match-scoped systems.</summary>
    public override void OnStartupServer()
    {
        _spawns.Start();   // begin injecting 2× enemy waves + scaling denizens
        _patron.Start();   // wake the Patron's attack/buff loops
    }

    // ── Event hooks ───────────────────────────────────────────────────────────────

    [GameEventHandler("player_death")]
    public HookResult OnPlayerDeath(GameEvent e)
    {
        // Timed enhancements end on death (DESIGN.md #8); relics persist.
        _relics.OnPlayerDeath(e);
        return HookResult.Continue;
    }

    /// <summary>
    /// World-loot pickups (gold statues / crates) are entity touches/breaks — route them to
    /// the loot system, which grants items or rolls a rare enhanced drop (DESIGN.md #4, #6).
    /// </summary>
    public override HookResult OnEntityStartTouch(EntityTouchEvent e)
    {
        _loot.OnEntityTouch(e);
        _upgrades.OnEntityTouch(e); // entering an Upgrade Station zone
        return HookResult.Continue;
    }
}
