using System.Numerics;
using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md §4 — the Hidden King. The *enemy* Patron (<c>npc_boss_tier3</c>, team 2 — NOT the heroes'
/// own team-3 Patron) reworked into a multi-phase boss. We resolve the live enemy Patron (cached from
/// <see cref="OnEntitySpawned"/>, fallback scan), split its health into
/// <see cref="BossRushConfig.BossHealthBars"/> bars, and on every bar lost we escalate (shorter cadence
/// + a transition ult).
///
/// Live test (2026-06-17) confirmed the native Patron owns its real ability entities —
/// <c>citadel_ability_tier3boss_{laser_beam,aoe_wave,rocket_barrage,drop_bombs}</c>. So each ult maps to
/// a real ability and, when <see cref="BossRushConfig.BossUseNativeAbilities"/> is on, we fire it via
/// <c>CCitadelAbilityComponent.ToggleActivate</c> (the component-agnostic activation path — we borrow any
/// player's component, since that native call keys off the ability entity, not the component). The
/// reliable <c>Hurt</c>-based simulation stays as the fallback / default until the native path is
/// confirmed in-game (dw_br_bosscast). Every attack is **range-gated** to the boss's engage range so it
/// can no longer hit heroes across the whole map. The "Rem — Naptime" sleep has no native equivalent, so
/// it stays a CC modifier (real Deadlock sleep/stun name TBD).
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

    // Placeholder self-buff modifier names — replace with real Deadlock modifiers once known.
    private static readonly string[] RandomBuffs =
    {
        "modifier_bossrush_patron_overcharge",
        "modifier_bossrush_patron_haste",
        "modifier_bossrush_patron_barrier",
    };

    private enum SimKind { Laser, LightningAoe, BarrageAoe, Sleep }

    /// <summary>One ult: a label, the real native ability to fire (null = no native equivalent), and the
    /// simulated effect used as fallback / when native casting is off.</summary>
    private sealed record UltDef(string Label, string? Native, SimKind Sim);

    private static readonly UltDef[] Rotation =
    {
        new("Hidden King — Laser",      "citadel_ability_tier3boss_laser_beam",     SimKind.Laser),
        new("Seven — Storm Cloud",      "citadel_ability_tier3boss_aoe_wave",       SimKind.LightningAoe),
        new("McGinnis — Heavy Barrage", "citadel_ability_tier3boss_rocket_barrage", SimKind.BarrageAoe),
        new("Hidden King — Drop Bombs", "citadel_ability_tier3boss_drop_bombs",     SimKind.BarrageAoe),
        new("Rem — Naptime",            null,                                       SimKind.Sleep),
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

    // ── Patron resolution (enemy team only) ─────────────────────────────────────────

    /// <summary>Capture the *enemy* Patron the moment it spawns; optionally size its total health.</summary>
    public void OnEntitySpawned(EntitySpawnedEvent e)
    {
        if (e.Entity.DesignerName != PatronDesignerName) return;

        // Health/team settle a tick after the spawn event — defer, then accept only the Hidden King (team 2).
        uint handle = e.Entity.EntityHandle;
        _timer.NextTick(() =>
        {
            var p = CBaseEntity.FromHandle(handle);
            if (p == null || !p.IsAlive || p.TeamNum != BossRushPlugin.EnemyTeam) return;
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

    /// <summary>The live enemy Patron — cached handle first (revalidated as team 2), else the highest-health
    /// enemy-team <c>npc_boss_tier3</c> (so dev-spawned test bosses win over the 12k native one).</summary>
    public CBaseEntity? FindPatron()
    {
        if (_patronHandle != 0)
        {
            var p = CBaseEntity.FromHandle(_patronHandle);
            if (p != null && p.IsAlive && p.TeamNum == BossRushPlugin.EnemyTeam) return p;
            _patronHandle = 0;
        }

        var found = Entities.All
            .Where(e => e.DesignerName == PatronDesignerName && e.IsAlive && e.TeamNum == BossRushPlugin.EnemyTeam)
            .OrderByDescending(e => e.MaxHealth)
            .FirstOrDefault();
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
        CastNextUlt(patron); // escalation felt instantly on the transition
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

    private void CastNextUlt(CBaseEntity patron)
    {
        var def = Rotation[_ultCursor % Rotation.Length];
        _ultCursor++;
        Cast(patron, def);
    }

    private void Cast(CBaseEntity patron, UltDef def)
    {
        // Prefer the real native ability when enabled and available; otherwise simulate the effect.
        if (_cfg.BossUseNativeAbilities && def.Native != null && TryCastNative(patron, def.Native, def.Label))
            return;

        switch (def.Sim)
        {
            case SimKind.Laser: FireLaser(patron); break;
            case SimKind.LightningAoe: FireAoe(patron, def.Label); break;
            case SimKind.BarrageAoe: FireBarrage(patron, def.Label); break;
            case SimKind.Sleep: FireSleep(patron, def.Label); break;
        }
    }

    // ── Native ability casting (decision #6) ───────────────────────────────────────

    /// <summary>Fire one of the Patron's own ability entities via the component-agnostic ToggleActivate
    /// path. Returns false (→ fall back to simulation) if the ability ent or a borrowable component is absent.</summary>
    private bool TryCastNative(CBaseEntity patron, string abilityDesigner, string label)
    {
        var ability = FindBossAbility(patron, abilityDesigner);
        if (ability == null) return false;
        var comp = BorrowAbilityComponent();
        if (comp == null) return false;

        comp.ToggleActivate(ability, true);
        Chat.PrintToChatAll($"[Boss Rush] Hidden King: {label}");
        patron.EmitSound(_cfg.PatronLaserSound);
        return true;
    }

    /// <summary>The boss's own copy of an ability ent (same team, nearest to the boss).</summary>
    private static CBaseEntity? FindBossAbility(CBaseEntity patron, string abilityDesigner) =>
        Entities.All
            .Where(e => e.DesignerName == abilityDesigner && e.TeamNum == patron.TeamNum)
            .OrderBy(e => Vector3.DistanceSquared(e.Position, patron.Position))
            .FirstOrDefault();

    /// <summary>Any living player's ability component — ToggleActivate keys off the ability ent, not this.</summary>
    private static CCitadelAbilityComponent? BorrowAbilityComponent() =>
        Players.GetAllPawns().FirstOrDefault(p => p.Health > 0)?.AbilityComponent;

    // ── Simulated ult effects (fallback / default) ─────────────────────────────────

    private void FireLaser(CBaseEntity patron)
    {
        var target = NearestHeroInRange(patron);
        if (target == null) return;

        var beam = CParticleSystem.Create(_cfg.PatronLaserParticle)
            .AtPosition(patron.Position)
            .WithControlPoint(1, target)
            .Spawn();
        if (beam != null) _timer.Once(1.Seconds(), () => beam.Destroy());

        patron.EmitSound(_cfg.PatronLaserSound);
        target.Hurt(CurrentUltDamage, attacker: patron, inflictor: patron);
    }

    /// <summary>Single AoE burst centred on an in-range hero (Seven-style).</summary>
    private void FireAoe(CBaseEntity patron, string label)
    {
        var focus = RandomHeroInRange(patron);
        if (focus == null) return;

        Vector3 center = focus.Position;
        Chat.PrintToChatAll($"[Boss Rush] Hidden King casts {label}!");
        SpawnFx(center);
        patron.EmitSound(_cfg.PatronLaserSound);
        DamageInRadius(patron, center, CurrentUltDamage);
    }

    /// <summary>Staggered multi-hit barrage over an in-range area (McGinnis / drop-bombs style).</summary>
    private void FireBarrage(CBaseEntity patron, string label)
    {
        var focus = RandomHeroInRange(patron);
        if (focus == null) return;

        Vector3 center = focus.Position;
        Chat.PrintToChatAll($"[Boss Rush] Hidden King casts {label}!");
        patron.EmitSound(_cfg.PatronLaserSound);

        uint handle = patron.EntityHandle;
        int hits = Math.Max(1, _cfg.BossBarrageHits);
        float perHit = CurrentUltDamage * 0.5f;
        for (int i = 0; i < hits; i++)
        {
            int delayMs = (int)(i * _cfg.BossBarrageIntervalSeconds * 1000);
            _timer.Once(delayMs.Milliseconds(), () =>
            {
                var p = CBaseEntity.FromHandle(handle);
                if (p == null || !p.IsAlive) return;
                SpawnFx(center);
                DamageInRadius(p, center, perHit);
            });
        }
    }

    /// <summary>The "Naptime" sleep: a CC modifier on an in-range hero + chip damage.</summary>
    private void FireSleep(CBaseEntity patron, string label)
    {
        var target = RandomHeroInRange(patron);
        if (target == null) return;

        Chat.PrintToChatAll($"[Boss Rush] Hidden King casts {label} — sleep!");
        patron.EmitSound(_cfg.PatronLaserSound);
        target.AddModifier(_cfg.BossSleepModifier); // placeholder CC; real Deadlock sleep/stun TBD
        target.Hurt(CurrentUltDamage * 0.25f, attacker: patron, inflictor: patron);
    }

    private void SpawnFx(Vector3 center)
    {
        var fx = CParticleSystem.Create(_cfg.PatronLaserParticle).AtPosition(center).Spawn();
        if (fx != null) _timer.Once(2.Seconds(), () => fx.Destroy());
    }

    private void DamageInRadius(CBaseEntity patron, Vector3 center, float dmg)
    {
        float r2 = _cfg.BossUltAoeRadius * _cfg.BossUltAoeRadius;
        foreach (var hero in LivingHeroes())
            if (Vector3.DistanceSquared(hero.Position, center) <= r2)
                hero.Hurt(dmg, attacker: patron, inflictor: patron);
    }

    private void RollBuff()
    {
        var patron = FindPatron();
        if (patron == null) return;
        patron.AddModifier(RandomBuffs[_rng.Next(RandomBuffs.Length)]); // TODO: real self-buffs + durations
    }

    // ── Target helpers (all range-gated to the boss's engage range) ─────────────────

    private static IEnumerable<CCitadelPlayerPawn> LivingHeroes() =>
        Players.GetAllPawns().Where(p => p.TeamNum == BossRushPlugin.HeroTeam && p.Health > 0);

    private IEnumerable<CCitadelPlayerPawn> HeroesInRange(CBaseEntity patron)
    {
        float r2 = _cfg.BossEngageRange * _cfg.BossEngageRange;
        return LivingHeroes().Where(h => Vector3.DistanceSquared(h.Position, patron.Position) <= r2);
    }

    private CBaseEntity? NearestHeroInRange(CBaseEntity patron)
    {
        CCitadelPlayerPawn? best = null;
        float bestDist = float.MaxValue;
        foreach (var h in HeroesInRange(patron))
        {
            float d = Vector3.DistanceSquared(h.Position, patron.Position);
            if (d < bestDist) { bestDist = d; best = h; }
        }
        return best;
    }

    private CBaseEntity? RandomHeroInRange(CBaseEntity patron)
    {
        var list = HeroesInRange(patron).ToList();
        return list.Count == 0 ? null : list[_rng.Next(list.Count)];
    }

    // ── Dev hooks (driven by the dw_br_boss* commands in DevTools) ──────────────────

    public string DebugStatus()
    {
        var p = FindPatron();
        if (p == null) return "patron=NONE (need a live team-2 npc_boss_tier3)";
        int inRange = HeroesInRange(p).Count();
        return $"patron hp={p.Health}/{p.MaxHealth} team={p.TeamNum} bars={_cfg.BossHealthBars} consumed={_barsConsumed} " +
               $"nextInterval={CurrentAttackInterval:F1}s ultDmg={CurrentUltDamage:F0} heroesInRange={inRange} native={_cfg.BossUseNativeAbilities}";
    }

    /// <summary>Fire one rotation ult now via the normal Cast path (sim unless native is enabled).</summary>
    public bool DebugFireUlt(int index)
    {
        var p = FindPatron();
        if (p == null) return false;
        int n = Rotation.Length;
        Cast(p, Rotation[((index % n) + n) % n]);
        return true;
    }

    /// <summary>Force the native ToggleActivate path on an ability matching <paramref name="abilitySubstr"/>.</summary>
    public string DebugCastNative(string abilitySubstr)
    {
        var patron = FindPatron();
        if (patron == null) return "no enemy Patron found";

        var ability = Entities.All.FirstOrDefault(e =>
            !string.IsNullOrEmpty(e.DesignerName) &&
            e.DesignerName.Contains("ability", StringComparison.OrdinalIgnoreCase) &&
            e.DesignerName.Contains(abilitySubstr, StringComparison.OrdinalIgnoreCase) &&
            e.TeamNum == patron.TeamNum);
        if (ability == null) return $"no enemy-team ability ent matching '{abilitySubstr}'";

        var comp = BorrowAbilityComponent();
        if (comp == null) return "no living hero to borrow an ability component from";

        comp.ToggleActivate(ability, true);
        return $"ToggleActivate('{ability.DesignerName}') sent on team {ability.TeamNum} — watch the boss in-game";
    }

    /// <summary>Promote the existing enemy Patron (resize to <paramref name="hp"/> if &gt; 0) and target it —
    /// avoids spawning a duplicate boss for testing.</summary>
    public bool DebugPromote(int hp)
    {
        var patron = FindPatron();
        if (patron == null) return false;
        if (hp > 0) { patron.MaxHealth = hp; patron.Health = hp; }
        _patronHandle = patron.EntityHandle;
        _barsConsumed = 0;
        return true;
    }
}
