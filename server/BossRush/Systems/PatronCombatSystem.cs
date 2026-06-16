using System.Numerics;
using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md #2 — the Patron (<c>npc_boss_tier3</c>) fights back. No "fire laser" API exists, so
/// it's assembled from the verified ScourgePlugin pattern: a repeating attack that picks a hero,
/// draws a beam particle (CP1 bound to the target), plays a sound, and deals damage that scales
/// with <see cref="GameRules.GameClock"/>. It also rolls a random self-buff modifier periodically.
/// </summary>
public sealed class PatronCombatSystem
{
    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;
    private IHandle? _laserLoop;
    private IHandle? _buffLoop;
    private readonly Random _rng = new();

    // Placeholder modifier names — replace with real Deadlock modifiers discovered in P3.
    private static readonly string[] RandomBuffs =
    {
        "modifier_bossrush_patron_overcharge",
        "modifier_bossrush_patron_haste",
        "modifier_bossrush_patron_barrier",
    };

    public PatronCombatSystem(BossRushConfig cfg, ITimer timer)
    {
        _cfg = cfg;
        _timer = timer;
    }

    public void Start()
    {
        Stop();
        _laserLoop = _timer.Every(((int)(_cfg.PatronLaserIntervalSeconds * 1000)).Milliseconds(), FireLaser);
        _buffLoop = _timer.Every(((int)_cfg.PatronBuffRollIntervalSeconds).Seconds(), RollBuff);
    }

    public void Stop()
    {
        _laserLoop?.Cancel(); _laserLoop = null;
        _buffLoop?.Cancel(); _buffLoop = null;
    }

    private float CurrentLaserDamage =>
        _cfg.PatronLaserBaseDamage + _cfg.PatronLaserDamagePerMinute * (GameRules.GameClock / 60f);

    private void FireLaser()
    {
        var patron = FindPatron();
        if (patron == null) return;

        var target = PickTarget(patron);
        if (target == null) return;

        // Beam: control point 0 = origin (spawn position), CP1 follows the target.
        var beam = CParticleSystem.Create(_cfg.PatronLaserParticle)
            .AtPosition(patron.Position)
            .WithControlPoint(1, target)
            .Spawn();
        if (beam != null)
            _timer.Once(1.Seconds(), () => beam.Destroy());

        patron.EmitSound(_cfg.PatronLaserSound);
        target.Hurt(CurrentLaserDamage, attacker: patron, inflictor: patron);
    }

    private void RollBuff()
    {
        var patron = FindPatron();
        if (patron == null) return;
        patron.AddModifier(RandomBuffs[_rng.Next(RandomBuffs.Length)]); // TODO(P3): durations via KV
    }

    /// <summary>Resolve the live Patron entity (verified designer name <c>npc_boss_tier3</c>).</summary>
    private static CBaseEntity? FindPatron()
    {
        // TODO(P0/P3): confirm the query helper name (e.g. Entities.FirstByDesignerName) against
        // the SDK, or cache the Patron handle from OnEntitySpawned where DesignerName == "npc_boss_tier3".
        return null;
    }

    /// <summary>Pick the nearest living hero to the Patron.</summary>
    private static CBaseEntity? PickTarget(CBaseEntity patron)
    {
        CCitadelPlayerPawn? best = null;
        float bestDist = float.MaxValue;
        foreach (var pawn in Players.GetAllPawns())
        {
            if (pawn.TeamNum != BossRushPlugin.HeroTeam || pawn.Health <= 0) continue;
            float d = Vector3.DistanceSquared(pawn.Position, patron.Position);
            if (d < bestDist) { bestDist = d; best = pawn; }
        }
        return best;
    }
}
