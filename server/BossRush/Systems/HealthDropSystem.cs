using System.Numerics;
using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md — Hidden King creeps drop a health pickup on death so fighting through the horde
/// sustains the heroes, at roughly 1/8 the value of a medic trooper's health pack.
///
/// We can't (yet) scale a pickup's heal value, so the "1/8" is approximated by chance: only
/// <see cref="BossRushConfig.HealthDropChance"/> (≈0.125) of creep kills drop a pack. Once we know
/// the pickup entity's heal field — or find a smaller pickup variant — we can switch to true
/// per-drop value scaling. The pickup designer name is map/game data: set
/// <see cref="BossRushConfig.HealthDropEntity"/> after confirming it in-game (empty = disabled).
/// </summary>
public sealed class HealthDropSystem
{
    private static readonly Random Rng = new();
    private readonly BossRushConfig _cfg;

    public HealthDropSystem(BossRushConfig cfg) => _cfg = cfg;

    public void OnEnemyTrooperKilled(Vector3 pos)
    {
        if (string.IsNullOrWhiteSpace(_cfg.HealthDropEntity)) return;
        if (Rng.NextDouble() > _cfg.HealthDropChance) return;

        var drop = CBaseEntity.CreateByDesignerName(_cfg.HealthDropEntity);
        if (drop == null) return;
        drop.Teleport(position: pos + new Vector3(0f, 0f, 24f));
        drop.Spawn();
    }
}
