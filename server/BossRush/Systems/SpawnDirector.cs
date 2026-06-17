using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md #1, #9, #10 — keeps heroes' troopers vanilla while giving the enemy side
/// <see cref="BossRushConfig.EnemyTrooperSpawnMultiplier"/>× spawns, doubles the lane Guardians,
/// and makes denizens (neutrals) start stronger and scale with match time.
///
/// We inject extra enemy waves at runtime so only one side is buffed (no symmetric VData edit,
/// no client install). Verified classnames: <c>npc_trooper</c>, <c>npc_boss_tier1</c> (Guardian).
/// </summary>
public sealed class SpawnDirector
{
    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;
    private IHandle? _waveLoop;

    public SpawnDirector(BossRushConfig cfg, ITimer timer)
    {
        _cfg = cfg;
        _timer = timer;
    }

    public void Start()
    {
        Stop();
        // Bonus enemy waves between the natural ones. (Natural trooper spawns stay on via convar.)
        _waveLoop = _timer.Every(BonusWaveInterval(), SpawnBonusEnemyWave);
        // Double the Guardians once the map's objectives exist.
        _timer.Once(5.Seconds(), DoubleGuardians);
    }

    public void Stop()
    {
        _waveLoop?.Cancel();
        _waveLoop = null;
    }

    private Duration BonusWaveInterval()
    {
        // Vanilla troopers spawn ~every 30s; the bonus cadence scales with the multiplier.
        // Placeholder until real cadence is measured in P1.
        var bonusPerMinute = MathF.Max(0f, _cfg.EnemyTrooperSpawnMultiplier - 1f) * 2f;
        var seconds = bonusPerMinute > 0 ? 60f / bonusPerMinute : 9999f;
        return ((int)seconds).Seconds();
    }

    private void SpawnBonusEnemyWave()
    {
        // Extra hostile troopers on top of the natural lane waves, via the game's own spawner —
        // raw CreateByDesignerName("npc_trooper")+Spawn() AVs the server (docs/VERIFIED_API.md §11–12).
        int grid = Math.Clamp((int)MathF.Round(_cfg.EnemyTrooperSpawnMultiplier), 2, 5);
        Troopers.SpawnGrid(grid);
    }

    private void DoubleGuardians()
    {
        if (_cfg.GuardiansPerLaneMultiplier <= 1) return;
        // TODO(P1): for each existing npc_boss_tier1 (Guardian), spawn (mult−1) more nearby,
        //   matching TeamNum/lane. Alternatively place duplicates in an edited map (client install).
        //   foreach (var g in Entities.ByDesignerName("npc_boss_tier1")) { ...clone near g... }
    }

    /// <summary>Buff denizens to start stronger and scale with match time (DESIGN.md #10).</summary>
    public void OnEntitySpawned(EntitySpawnedEvent e)
    {
        var name = e.Entity.DesignerName;
        // Denizens = neutral camps. TODO(P1): confirm their designer name(s); npc_trooper is lane,
        // not neutral. Apply a scaling modifier sized by GameRules.GameClock.
        if (name is not ("npc_neutral" or "npc_denizen")) return;

        float minutes = GameRules.GameClock / 60f;
        float strength = _cfg.DenizenBaseStrengthMultiplier + _cfg.DenizenStrengthPerMinute * minutes;

        using var kv = new KeyValues3();
        kv.SetFloat("strength", strength);
        e.Entity.AddModifier("modifier_bossrush_denizen_scaling", kv); // TODO(P1): author this modifier
    }
}
