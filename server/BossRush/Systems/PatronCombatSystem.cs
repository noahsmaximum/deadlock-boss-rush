using DeadworksManaged.Api; // ⚠️ provisional — verify in P0

namespace BossRush;

/// <summary>
/// DESIGN.md #2 — the Patron fights back. There is no "fire laser" API, so we assemble it from
/// primitives: a repeating attack that picks a target, draws a beam particle, plays a sound,
/// and deals damage that scales with match time. Periodically the Patron also rolls a random
/// self-buff modifier.
/// </summary>
public sealed class PatronCombatSystem
{
    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;
    private IHandle? _laserLoop;
    private IHandle? _buffLoop;
    private float _elapsedMinutes;

    // Random buffs the Patron can roll on itself. Names are placeholders — replace with real
    // Deadlock modifier names discovered in P3.
    private static readonly string[] RandomBuffs =
    {
        "modifier_bossrush_patron_overcharge",
        "modifier_bossrush_patron_haste",
        "modifier_bossrush_patron_barrier",
    };

    private readonly Random _rng = new();

    public PatronCombatSystem(BossRushConfig cfg, ITimer timer)
    {
        _cfg = cfg;
        _timer = timer;
    }

    public void Start()
    {
        Stop();
        _laserLoop = _timer.Every(_cfg.PatronLaserIntervalSeconds.Seconds(), FireLaser);
        _buffLoop = _timer.Every(_cfg.PatronBuffRollIntervalSeconds.Seconds(), RollBuff);
        _timer.Every(1.Minutes(), () => _elapsedMinutes += 1f);
    }

    public void Stop()
    {
        _laserLoop?.Cancel(); _laserLoop = null;
        _buffLoop?.Cancel(); _buffLoop = null;
        _elapsedMinutes = 0;
    }

    private float CurrentLaserDamage =>
        _cfg.PatronLaserBaseDamage + _cfg.PatronLaserDamagePerMinute * _elapsedMinutes;

    private void FireLaser()
    {
        var patron = FindPatron();
        if (patron is null) return;

        var target = PickTarget(patron);
        if (target is null) return;

        // Beam particle from Patron → target (DESIGN.md confirms CParticleSystem fluent API).
        CParticleSystem.Create(_cfg.PatronLaserParticle)
            .AtPosition(patron.Position)
            .WithControlPoint(1, target)
            .Spawn()
            .Start();

        patron.EmitSound(_cfg.PatronLaserSound);

        // Deal the damage with the Patron as attacker (DESIGN.md confirms Hurt(...)).
        target.Hurt(CurrentLaserDamage, attacker: patron, inflictor: patron);
    }

    private void RollBuff()
    {
        var patron = FindPatron();
        if (patron is null) return;
        var buff = RandomBuffs[_rng.Next(RandomBuffs.Length)];
        patron.AddModifier(buff); // TODO(P3): tune durations/stacks via KeyValues3
    }

    // TODO(P3): resolve the real Patron entity. GameRules exposes mid-boss/patron state; the
    // entity is likely queryable by classname/designer-name (Entities.ByClass/ByName).
    private CBaseEntity? FindPatron() => null;

    // TODO(P3): pick the nearest / lowest-HP hero in range of the Patron.
    private CBaseEntity? PickTarget(CBaseEntity patron) => null;
}
