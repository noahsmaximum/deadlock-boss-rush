using DeadworksManaged.Api; // ⚠️ provisional — verify in P0

namespace BossRush;

/// <summary>
/// DESIGN.md #9 &amp; #10 — keeps the heroes' trooper spawns vanilla while giving the enemy side
/// <see cref="BossRushConfig.EnemyTrooperSpawnMultiplier"/>× spawns, and makes denizens
/// (neutrals) start stronger and scale up over match time to track hero power.
///
/// We inject *extra* enemy waves at runtime (rather than editing symmetric VData) so only one
/// side is buffed and nothing is forced onto clients.
/// </summary>
public sealed class SpawnDirector
{
    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;
    private IHandle? _waveLoop;
    private IHandle? _scaleLoop;
    private float _elapsedMinutes;

    public SpawnDirector(BossRushConfig cfg, ITimer timer)
    {
        _cfg = cfg;
        _timer = timer;
    }

    public void Start()
    {
        Stop();
        // Extra enemy waves: spawn (multiplier − 1) bonus waves between the natural ones.
        // TODO(P1): find the trooper classname + per-lane enemy spawn points, then spawn
        // bonus troopers in sync with the natural wave cadence.
        _waveLoop = _timer.Every(WaveInterval(), SpawnBonusEnemyWave);

        // Denizen scaling tick (once per minute).
        _scaleLoop = _timer.Every(1.Minutes(), ScaleDenizens);
    }

    public void Stop()
    {
        _waveLoop?.Cancel(); _waveLoop = null;
        _scaleLoop?.Cancel(); _scaleLoop = null;
        _elapsedMinutes = 0;
    }

    private Duration WaveInterval()
    {
        // Vanilla troopers spawn roughly every ~30s; bonus cadence scales with the multiplier.
        // Placeholder until the real cadence is measured in P1.
        var perMinuteBonus = Math.Max(0f, _cfg.EnemyTrooperSpawnMultiplier - 1f) * 2f;
        var seconds = perMinuteBonus > 0 ? 60f / perMinuteBonus : 9999f;
        return seconds.Seconds();
    }

    private void SpawnBonusEnemyWave()
    {
        // TODO(P1): for each enemy lane spawn point:
        //   var t = CBaseEntity.CreateByName("<trooper_classname>");
        //   t.Position = point; t.TeamNum = EnemyTeam; t.Spawn();
    }

    private void ScaleDenizens()
    {
        _elapsedMinutes += 1f;
        var strength = _cfg.DenizenBaseStrengthMultiplier + _cfg.DenizenStrengthPerMinute * _elapsedMinutes;

        // TODO(P1): query living denizens and (re)apply a strength modifier. e.g.
        //   foreach (var npc in Entities.ByClass<...>())
        //   {
        //       var kv = new KeyValues3();
        //       kv.SetFloat("strength", strength);
        //       npc.AddModifier("modifier_bossrush_denizen_scaling", kv);
        //   }
        _ = strength;
    }
}
