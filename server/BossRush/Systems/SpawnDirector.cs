using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md #9, #10 — the enemy threat is the Hidden King's own lane troopers. Rather than
/// spawning our own (raw <c>npc_trooper</c> spawns AV; near-player console spawns feel wrong), we
/// let the game march them out of the Hidden King's lanes and make them progressively deadlier:
/// every enemy-team trooper is buffed as it spawns, scaling with match time.
/// </summary>
public sealed class SpawnDirector
{
    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;

    public SpawnDirector(BossRushConfig cfg, ITimer timer)
    {
        _cfg = cfg;
        _timer = timer;
    }

    public void Start() { }
    public void Stop() { }

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
