using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md #9, #10 — keep the Hidden King ahead of the Archmother. The natural lane waves spawn
/// equal troopers for both sides, so we pour extra Hidden King troopers down their lanes on a steady
/// reinforcement loop (~2× the Archmother), and buff each enemy trooper as it spawns so they get
/// deadlier with match time. Enemies come from the Hidden King's lanes via the zipline spawner
/// (raw <c>npc_trooper</c> spawns AV; see docs/VERIFIED_API.md §11–12).
/// </summary>
public sealed class SpawnDirector
{
    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;
    private IHandle? _reinforce;

    public SpawnDirector(BossRushConfig cfg, ITimer timer)
    {
        _cfg = cfg;
        _timer = timer;
    }

    public void Start()
    {
        _reinforce?.Cancel();
        _reinforce = _timer.Every(((int)_cfg.ReinforcementIntervalSeconds).Seconds(), Reinforce);
    }

    public void Stop()
    {
        _reinforce?.Cancel();
        _reinforce = null;
    }

    /// <summary>Steady stream of extra Hidden King troopers down their lanes (keeps them ~2× the Archmother).</summary>
    private void Reinforce() =>
        LaneTroopers.Spawn(BossRushPlugin.EnemyTeam, _cfg.ReinforcementSquadSize, _cfg.HiddenKingLanesCsv);

    /// <summary>Buff the Hidden King's lane troopers as they spawn, scaling with match time.</summary>
    public void OnEntitySpawned(EntitySpawnedEvent e)
    {
        var ent = e.Entity;
        if (ent.TeamNum != BossRushPlugin.EnemyTeam) return;
        if (!ent.DesignerName.Contains("trooper", StringComparison.OrdinalIgnoreCase)) return;

        // Defer one tick so the trooper's health is initialized before we scale it.
        uint handle = ent.EntityHandle;
        _timer.NextTick(() =>
        {
            var t = CBaseEntity.FromHandle(handle);
            if (t == null || !t.IsAlive || t.MaxHealth <= 0) return;

            float minutes = GameRules.GameClock / 60f;
            float mult = _cfg.DenizenBaseStrengthMultiplier + _cfg.DenizenStrengthPerMinute * minutes;
            if (mult <= 1f) return;

            t.MaxHealth = (int)(t.MaxHealth * mult);
            t.Health = t.MaxHealth;
        });
    }
}
