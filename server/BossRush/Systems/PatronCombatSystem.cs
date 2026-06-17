using System.Numerics;
using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md §4 — the Hidden King. The enemy faction's Patron (<c>npc_boss_tier3</c>) reworked into
/// a multi-phase boss. We resolve the live Patron (cached from <see cref="OnEntitySpawned"/>, with a
/// fallback scan), split its health into <see cref="BossRushConfig.BossHealthBars"/> "bars", and on
/// every bar lost we escalate: the attack cadence shortens and a transition fires. Between transitions
/// the King cycles a rotation of hero-ult-themed attacks — a single-target laser, a Seven-style
/// lightning AoE, a McGinnis-style barrage AoE, and a Rem "Naptime" sleep — all built from the verified
/// primitives the existing code already uses (<c>Hurt</c> / <c>AddModifier</c> / <c>CParticleSystem</c>
/// / <c>EmitSound</c>), so they work today.
///
/// Whether the King can instead fire the *real* native abilities (decision #6 — the
/// <c>citadel_ability_tier3boss_*</c> child ents via <c>AcceptInput</c>, or real hero ult ids) is an
/// open live-test; <c>dw_br_bossinfo</c> / <c>dw_br_bossinput</c> probe it. Until that's answered we
/// deliver the *effect* of each ult with reliable primitives and swap in the real cast once known.
/// </summary>
public sealed class PatronCombatSystem
{
    public const string PatronDesignerName = "npc_boss_tier3";

    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;
    private readonly Random _rng = new();

    private IHandle? _attackLoop;
    private IHandle? _phasePoll;
    private IHandle? _buffLoop;
    private bool _running;

    private uint _patronHandle;
    private int _barsConsumed; // health bars the King has lost so far (0..BossHealthBars)
    private int _ultCursor;    // rotation position

    // Placeholder self-buff modifier names — replace with real Deadlock modifiers once known (P3 live-test).
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
        _running = true;
        _barsConsumed = 0;
        _ultCursor = 0;
        _patronHandle = 0;
        _phasePoll = _timer.Every(((int)(_cfg.BossPhasePollSeconds * 1000)).Milliseconds(), PollPhase);
        _buffLoop = _timer.Every(((int)_cfg.PatronBuffRollIntervalSeconds).Seconds(), RollBuff);
        ScheduleNextAttack();
    }

    public void Stop()
    {
        _running = false;
        _attackLoop?.Cancel(); _attackLoop = null;
        _phasePoll?.Cancel(); _phasePoll = null;
        _buffLoop?.Cancel(); _buffLoop = null;
    }

    // ── Patron resolution ──────────────────────────────────────────────────────────

    /// <summary>Capture the Patron handle the moment it spawns; optionally size its total health.</summary>
    public void OnEntitySpawned(EntitySpawnedEvent e)
    {
        if (e.Entity.DesignerName != PatronDesignerName) return;

        // Health/team settle a tick after the spawn event (same as SpawnDirector) — defer the setup.
        uint handle = e.Entity.EntityHandle;
        _timer.NextTick(() =>
        {
            var p = CBaseEntity.FromHandle(handle);
            if (p == null || !p.IsAlive) return;
            _patronHandle = handle;
            _barsConsumed = 0;
            if (_cfg.BossMaxHealth > 0)
            {
                p.MaxHealth = _cfg.BossMaxHealth;
                p.Health = _cfg.BossMaxHealth;
            }
            Chat.PrintToChatAll($"[Boss Rush] The Hidden King rises — {_cfg.BossHealthBars} health bars. Bring it down.");
        });
    }

    /// <summary>Resolve the live Patron (cached handle first, then a scan for hot-reload / pre-arm spawn).</summary>
    public CBaseEntity? FindPatron()
    {
        if (_patronHandle != 0)
        {
            var p = CBaseEntity.FromHandle(_patronHandle);
            if (p != null && p.IsAlive) return p;
            _patronHandle = 0;
        }

        var found = Entities.All.FirstOrDefault(e => e.DesignerName == PatronDesignerName && e.IsAlive);
        if (found != null) _patronHandle = found.EntityHandle;
        return found;
    }

    // ── Phase / health-bar tracking ──────────────────────────────────────────────

    private void PollPhase()
    {
        var patron = FindPatron();
        if (patron == null) return;

        int max = patron.MaxHealth;
        if (max <= 0) return;

        float frac = Math.Clamp((float)patron.Health / max, 0f, 1f);
        int bars = Math.Max(1, _cfg.BossHealthBars);
        int remaining = Math.Max(0, (int)MathF.Ceiling(frac * bars));
        int consumed = bars - remaining;

        if (consumed > _barsConsumed)
        {
            _barsConsumed = consumed;
            OnBarLost(patron, remaining);
        }
    }

    private void OnBarLost(CBaseEntity patron, int barsRemaining)
    {
        Chat.PrintToChatAll(barsRemaining > 0
            ? $"[Boss Rush] The Hidden King roars — {barsRemaining} health bar(s) left, and angrier."
            : "[Boss Rush] The Hidden King is breaking!");
        // The escalation is felt instantly: unleash an ult on the transition, then the loop keeps going faster.
        CastNextUlt(patron);
    }

    // ── Escalating attack loop (self-rescheduling so the interval shrinks each phase) ──

    private void ScheduleNextAttack()
    {
        if (!_running) return;
        int ms = Math.Max(100, (int)(CurrentAttackInterval * 1000));
        _attackLoop = _timer.Once(ms.Milliseconds(), () =>
        {
            if (!_running) return;
            var patron = FindPatron();
            if (patron != null) CastNextUlt(patron);
            ScheduleNextAttack();
        });
    }

    /// <summary>Cadence shrinks from <c>BossAttackIntervalSeconds</c> toward <c>…MinSeconds</c> as bars fall.</summary>
    private float CurrentAttackInterval
    {
        get
        {
            int span = Math.Max(1, _cfg.BossHealthBars - 1);
            float f = Math.Clamp((float)_barsConsumed / span, 0f, 1f);
            return _cfg.BossAttackIntervalSeconds
                 + (_cfg.BossAttackIntervalMinSeconds - _cfg.BossAttackIntervalSeconds) * f;
        }
    }

    private float CurrentUltDamage =>
        _cfg.PatronLaserBaseDamage
        + _cfg.PatronLaserDamagePerMinute * (GameRules.GameClock / 60f)
        + _cfg.BossUltDamagePerBar * _barsConsumed;

    // ── The ult rotation ──────────────────────────────────────────────────────────

    private enum UltKind { Laser, LightningAoe, BarrageAoe, Sleep }

    private static readonly UltKind[] Rotation =
        { UltKind.Laser, UltKind.LightningAoe, UltKind.BarrageAoe, UltKind.Sleep };

    private void CastNextUlt(CBaseEntity patron)
    {
        var kind = Rotation[_ultCursor % Rotation.Length];
        _ultCursor++;
        Cast(patron, kind);
    }

    private void Cast(CBaseEntity patron, UltKind kind)
    {
        switch (kind)
        {
            case UltKind.Laser: FireLaser(patron); break;
            case UltKind.LightningAoe: FireAoe(patron, "Seven — Storm Cloud"); break;
            case UltKind.BarrageAoe: FireAoe(patron, "McGinnis — Heavy Barrage"); break;
            case UltKind.Sleep: FireSleep(patron, "Rem — Naptime"); break;
        }
    }

    /// <summary>Single-target beam at the nearest hero (the original Patron-laser pattern).</summary>
    private void FireLaser(CBaseEntity patron)
    {
        var target = PickNearestHero(patron);
        if (target == null) return;

        var beam = CParticleSystem.Create(_cfg.PatronLaserParticle)
            .AtPosition(patron.Position)
            .WithControlPoint(1, target)
            .Spawn();
        if (beam != null) _timer.Once(1.Seconds(), () => beam.Destroy());

        patron.EmitSound(_cfg.PatronLaserSound);
        target.Hurt(CurrentUltDamage, attacker: patron, inflictor: patron);
    }

    /// <summary>Area ult: lands on a random hero; everyone within the radius takes the hit.</summary>
    private void FireAoe(CBaseEntity patron, string label)
    {
        var focus = PickRandomHero();
        if (focus == null) return;

        Vector3 center = focus.Position;
        Chat.PrintToChatAll($"[Boss Rush] Hidden King casts {label}!");

        var fx = CParticleSystem.Create(_cfg.PatronLaserParticle).AtPosition(center).Spawn();
        if (fx != null) _timer.Once(2.Seconds(), () => fx.Destroy());
        patron.EmitSound(_cfg.PatronLaserSound);

        float r2 = _cfg.BossUltAoeRadius * _cfg.BossUltAoeRadius;
        foreach (var hero in LivingHeroes())
            if (Vector3.DistanceSquared(hero.Position, center) <= r2)
                hero.Hurt(CurrentUltDamage, attacker: patron, inflictor: patron);
    }

    /// <summary>The "Naptime" sleep: a CC modifier on a random hero (real Deadlock CC TBD), plus chip damage.</summary>
    private void FireSleep(CBaseEntity patron, string label)
    {
        var target = PickRandomHero();
        if (target == null) return;

        Chat.PrintToChatAll($"[Boss Rush] Hidden King casts {label} — sleep!");
        patron.EmitSound(_cfg.PatronLaserSound);
        target.AddModifier(_cfg.BossSleepModifier); // placeholder CC; swap for the real sleep/stun (decision #6)
        target.Hurt(CurrentUltDamage * 0.25f, attacker: patron, inflictor: patron);
    }

    private void RollBuff()
    {
        var patron = FindPatron();
        if (patron == null) return;
        patron.AddModifier(RandomBuffs[_rng.Next(RandomBuffs.Length)]); // TODO: real self-buffs + durations via KV
    }

    // ── Target helpers ────────────────────────────────────────────────────────────

    private static IEnumerable<CCitadelPlayerPawn> LivingHeroes() =>
        Players.GetAllPawns().Where(p => p.TeamNum == BossRushPlugin.HeroTeam && p.Health > 0);

    private static CBaseEntity? PickNearestHero(CBaseEntity patron)
    {
        CCitadelPlayerPawn? best = null;
        float bestDist = float.MaxValue;
        foreach (var pawn in LivingHeroes())
        {
            float d = Vector3.DistanceSquared(pawn.Position, patron.Position);
            if (d < bestDist) { bestDist = d; best = pawn; }
        }
        return best;
    }

    private CBaseEntity? PickRandomHero()
    {
        var heroes = LivingHeroes().ToList();
        return heroes.Count == 0 ? null : heroes[_rng.Next(heroes.Count)];
    }

    // ── Dev hooks (driven by the dw_br_boss* commands in DevTools) ──────────────────

    /// <summary>One-line status for dw_br_bossinfo / dw_br_bossult.</summary>
    public string DebugStatus()
    {
        var p = FindPatron();
        return p == null
            ? "patron=NONE (spawn npc_boss_tier3 first)"
            : $"patron hp={p.Health}/{p.MaxHealth} bars={_cfg.BossHealthBars} consumed={_barsConsumed} " +
              $"nextInterval={CurrentAttackInterval:F1}s ultDmg={CurrentUltDamage:F0}";
    }

    /// <summary>Fire one ult from the rotation now (dev). Returns false if no Patron is live.</summary>
    public bool DebugFireUlt(int index)
    {
        var p = FindPatron();
        if (p == null) return false;
        int n = Rotation.Length;
        Cast(p, Rotation[((index % n) + n) % n]);
        return true;
    }
}
