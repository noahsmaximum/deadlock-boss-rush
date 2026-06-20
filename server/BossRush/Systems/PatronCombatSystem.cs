using System.Numerics;
using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md §4 — the Hidden King. The *enemy* Patron (<c>npc_boss_tier3</c>, team 2 — NOT the heroes'
/// own team-3 Patron) reworked into a multi-phase boss. We resolve the live enemy Patron (cached from
/// <see cref="OnEntitySpawned"/>, fallback scan), split its health into
/// <see cref="BossRushConfig.BossHealthBars"/> bars, and on every bar lost we escalate (shorter cadence
/// + a transition ult; Phase 2 forced at the midpoint).
///
/// The boss fires its REAL abilities through its own AI via the game's <c>citadel_boss_tier_3_test_*</c>
/// cvars (discovered live 2026-06-17) — real VFX, targeting, line-of-sight (dodgeable behind cover) and
/// durations. That replaced the dead-end managed <c>ToggleActivate</c> path. The simulated
/// <c>Hurt</c>-based effects remain as a fallback (used when <see cref="BossRushConfig.BossUseNativeAbilities"/>
/// is off, or a cvar is missing); they lack VFX/LoS, so native is the default. The "Rem — Naptime" sleep
/// has no native boss equivalent, so it applies Rem's real shipped sleep modifier `modifier_familiar_asleep`. Every attack is
/// range-gated to the boss's engage range.
/// </summary>
public sealed class PatronCombatSystem
{
    public const string PatronDesignerName = "npc_boss_tier3";

    // The boss's real abilities are triggered by these game cvars (set 1 → fire via AI).
    private const string CvarPhase2 = "citadel_boss_tier_3_testing_enter_phase2";
    private static readonly Dictionary<string, string> NativeAbilityCvars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["laser"]        = "citadel_boss_tier_3_test_laser",
        ["barrage"]      = "citadel_boss_tier_3_test_rocketbarrage",
        ["rocketbarrage"]= "citadel_boss_tier_3_test_rocketbarrage",
        ["bomb"]         = "citadel_boss_tier_3_test_bomb",
        ["bombs"]        = "citadel_boss_tier_3_test_bomb",
        ["smash"]        = "citadel_boss_tier_3_test_arm_smash",
        ["armsmash"]     = "citadel_boss_tier_3_test_arm_smash",
        ["shrine"]       = "citadel_boss_tier_3_test_shrine_attack",
        ["shrineattack"] = "citadel_boss_tier_3_test_shrine_attack",
        ["phase2"]       = CvarPhase2,
    };

    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;
    private readonly Random _rng = new();

    private IHandle? _attackLoop;
    private IHandle? _phasePoll;
    private IHandle? _buffLoop;
    private bool _running;

    private uint _patronHandle;
    private int _barsConsumed;     // health bars the King has lost so far (0..BossHealthBars)
    private int _ultCursor;        // rotation position
    private bool _phase2Triggered; // native Phase 2 forced once at the midpoint

    // Placeholder self-buff modifier names — replace with real Deadlock modifiers once known.
    private static readonly string[] RandomBuffs =
    {
        "modifier_bossrush_patron_overcharge",
        "modifier_bossrush_patron_haste",
        "modifier_bossrush_patron_barrier",
    };

    private enum SimKind { Laser, LightningAoe, BarrageAoe, Sleep }

    /// <summary>One ult: a label, the game cvar that fires the REAL ability (null = no native equivalent),
    /// and the simulated effect used as fallback.</summary>
    private sealed record UltDef(string Label, string? NativeCvar, SimKind Sim);

    // Live test: laser/shrine fire in phase 1; barrage/bomb/smash don't — they appear to be phase-2-gated
    // on the native boss. So phase 1 uses only the confirmed kit (+ a guaranteed sim sleep for pressure);
    // the heavy kit only enters the rotation once Phase 2 is forced at the midpoint bar.
    private static readonly UltDef[] Phase1Rotation =
    {
        new("Hidden King — Laser",         "citadel_boss_tier_3_test_laser",         SimKind.Laser),
        new("Hidden King — Shrine Attack", "citadel_boss_tier_3_test_shrine_attack", SimKind.LightningAoe),
        new("Rem — Naptime",               null,                                     SimKind.Sleep),
    };

    private static readonly UltDef[] Phase2Rotation =
    {
        new("Hidden King — Laser",         "citadel_boss_tier_3_test_laser",         SimKind.Laser),
        new("McGinnis — Rocket Barrage",   "citadel_boss_tier_3_test_rocketbarrage", SimKind.BarrageAoe),
        new("Hidden King — Bombs",         "citadel_boss_tier_3_test_bomb",          SimKind.BarrageAoe),
        new("Hidden King — Arm Smash",     "citadel_boss_tier_3_test_arm_smash",     SimKind.LightningAoe),
        new("Hidden King — Shrine Attack", "citadel_boss_tier_3_test_shrine_attack", SimKind.LightningAoe),
        new("Rem — Naptime",               null,                                     SimKind.Sleep),
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
        _phase2Triggered = false;
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

        uint handle = e.Entity.EntityHandle;
        _timer.NextTick(() =>
        {
            var p = CBaseEntity.FromHandle(handle);
            if (p == null || !p.IsAlive || p.TeamNum != BossRushPlugin.EnemyTeam) return;
            _patronHandle = handle;
            _barsConsumed = 0;
            _phase2Triggered = false;
            if (_cfg.BossMaxHealth > 0)
            {
                p.MaxHealth = _cfg.BossMaxHealth;
                p.Health = _cfg.BossMaxHealth;
            }
            Chat.PrintToChatAll($"[Boss Rush] The Hidden King rises — {_cfg.BossHealthBars} health bars. Bring it down.");
        });
    }

    /// <summary>The live enemy Patron — cached handle (revalidated as team 2), else the highest-health
    /// enemy-team <c>npc_boss_tier3</c>.</summary>
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

        // Force the native Phase 2 once we're halfway down.
        if (_cfg.BossUseNativeAbilities && !_phase2Triggered && _barsConsumed >= Math.Max(1, _cfg.BossHealthBars / 2))
        {
            ConVar.Find(CvarPhase2)?.SetInt(1);
            _phase2Triggered = true;
            Chat.PrintToChatAll("[Boss Rush] The Hidden King enters Phase 2!");
        }

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
        var active = _phase2Triggered ? Phase2Rotation : Phase1Rotation;
        var def = active[_ultCursor % active.Length];
        _ultCursor++;
        Cast(patron, def);
    }

    private void Cast(CBaseEntity patron, UltDef def)
    {
        // Prefer the boss's REAL ability (fired via its test cvar) when enabled; else simulate.
        if (_cfg.BossUseNativeAbilities && def.NativeCvar != null && FireNativeAbility(def.NativeCvar, def.Label))
            return;

        switch (def.Sim)
        {
            case SimKind.Laser: FireLaser(patron); break;
            case SimKind.LightningAoe: FireAoe(patron, def.Label); break;
            case SimKind.BarrageAoe: FireBarrage(patron, def.Label); break;
            case SimKind.Sleep: FireSleep(patron, def.Label); break;
        }
    }

    // ── Native ability firing (via the boss's own test cvars) ───────────────────────

    /// <summary>Set the boss ability's test cvar to 1 (fires through the boss AI), then reset it so the
    /// next trigger fires again. Returns false (→ fall back to simulation) if the cvar isn't found.</summary>
    private bool FireNativeAbility(string cvar, string label)
    {
        var cv = ConVar.Find(cvar);
        if (cv == null) return false;

        cv.SetInt(1);
        if (_cfg.BossNativeResetSeconds > 0)
            _timer.Once(((int)(_cfg.BossNativeResetSeconds * 1000)).Milliseconds(), () => ConVar.Find(cvar)?.SetInt(0));

        Chat.PrintToChatAll($"[Boss Rush] Hidden King: {label}");
        return true;
    }

    // ── Simulated ult effects (fallback / when native is off) ──────────────────────

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

    /// <summary>Staggered multi-hit barrage over an in-range area (defaults ≈10s — config'd).</summary>
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

    private void FireSleep(CBaseEntity patron, string label)
    {
        var target = RandomHeroInRange(patron);
        if (target == null) return;

        Chat.PrintToChatAll($"[Boss Rush] Hidden King casts {label}!");
        patron.EmitSound(_cfg.PatronLaserSound);
        // Rem's real sleep (modifier_familiar_asleep) needs its VData loaded server-side — only possible
        // by shipping it in a server-loaded VPK (P4). Precaching Rem does NOT register it. Until the addon
        // ships it, applying it just logs "VData not found", so it's gated off; the ult still chips.
        if (_cfg.BossApplySleepModifier)
        {
            using var kv = new KeyValues3();
            kv.SetFloat("duration", _cfg.BossSleepDurationSeconds);
            target.AddModifier(_cfg.BossSleepModifier, kv, caster: patron);
        }
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

    /// <summary>Fire one rotation ult now via the normal Cast path (native if enabled, else sim).</summary>
    public bool DebugFireUlt(int index)
    {
        var p = FindPatron();
        if (p == null) return false;
        int n = Phase2Rotation.Length;
        Cast(p, Phase2Rotation[((index % n) + n) % n]);
        return true;
    }

    /// <summary>Fire a real boss ability by key (laser/barrage/bomb/smash/shrine/phase2) or raw cvar.</summary>
    public string DebugFireNative(string key, float resetSec)
    {
        string cvar = NativeAbilityCvars.TryGetValue(key, out var c) ? c : key;
        var cv = ConVar.Find(cvar);
        if (cv == null) return $"cvar not found: {cvar}";

        cv.SetInt(1);
        if (resetSec > 0)
            _timer.Once(((int)(resetSec * 1000)).Milliseconds(), () => ConVar.Find(cvar)?.SetInt(0));
        return $"set {cvar}=1" + (resetSec > 0 ? $" (auto-reset to 0 in {resetSec:F1}s)" : " (no auto-reset)");
    }

    /// <summary>Promote the existing enemy Patron (resize to <paramref name="hp"/> if &gt; 0) and target it.</summary>
    public bool DebugPromote(int hp)
    {
        var patron = FindPatron();
        if (patron == null) return false;
        if (hp > 0) { patron.MaxHealth = hp; patron.Health = hp; }
        _patronHandle = patron.EntityHandle;
        _barsConsumed = 0;
        _phase2Triggered = false;
        return true;
    }
}
