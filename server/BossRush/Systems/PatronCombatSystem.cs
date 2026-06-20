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
    private int _bossPool;         // intended total health — survives native phase resets that wipe MaxHealth
    private float _lastFrac = 1f;  // last polled health fraction, used to restore the pool after a native reset

    // Placeholder self-buff modifier names — replace with real Deadlock modifiers once known.
    private static readonly string[] RandomBuffs =
    {
        "modifier_bossrush_patron_overcharge",
        "modifier_bossrush_patron_haste",
        "modifier_bossrush_patron_barrier",
    };

    private enum SimKind { Laser, LightningAoe, BarrageAoe, Sleep, ChargeBlast }

    /// <summary>One ult: a label, the game cvar that fires the REAL ability (null = no native equivalent),
    /// and the simulated effect used as fallback.</summary>
    private sealed record UltDef(string Label, string? NativeCvar, SimKind Sim);

    // The rotation casts hero-fantasy ults as scripted sims with REAL shipped particles (the boss can't
    // actually cast hero abilities). NativeCvar is the boss's own equivalent ability, forced only when
    // BossUseNativeAbilities is on (off by default — see config). The boss owns every hit in the kill feed
    // (Hurt(attacker: patron)), which sidesteps spawning real hero entities just to attribute damage.
    private static readonly UltDef[] Rotation =
    {
        new("Hidden King — Laser",       "citadel_boss_tier_3_test_laser",         SimKind.Laser),
        new("McGinnis — Rocket Barrage", "citadel_boss_tier_3_test_rocketbarrage", SimKind.BarrageAoe),
        new("Seven — Storm Cloud",       null,                                     SimKind.LightningAoe),
        new("Hidden King — Bomb Blast",  "citadel_boss_tier_3_test_bomb",          SimKind.ChargeBlast),
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
        _bossPool = 0;
        _lastFrac = 1f;
        _phasePoll = _timer.Every(((int)(_cfg.BossPhasePollSeconds * 1000)).Milliseconds(), PollPhase);
        // Self-buffs are OFF until real modifier names exist — the placeholders just spam "VData not found".
        if (_cfg.BossSelfBuffsEnabled)
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
            _lastFrac = 1f;
            if (_cfg.BossMaxHealth > 0)
            {
                p.MaxHealth = _cfg.BossMaxHealth;
                p.Health = _cfg.BossMaxHealth;
                _bossPool = _cfg.BossMaxHealth;
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

        // Native phase transitions (e.g. forcing Phase 2) reset the boss to its native health pool (~12k),
        // wiping our inflated pool. Detect the collapse and restore our pool at the last known fraction so
        // the bars stay consistent instead of snapping to a tiny real value.
        if (_bossPool > 0 && max < _bossPool)
        {
            patron.MaxHealth = _bossPool;
            patron.Health = Math.Max(1, (int)(_lastFrac * _bossPool));
            max = _bossPool;
        }

        float frac = Math.Clamp((float)patron.Health / max, 0f, 1f);
        _lastFrac = frac;
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

        // Force the native Phase 2 once we're halfway down. OFF by default: the native transition resets the
        // boss's health to its small native pool — PollPhase restores our inflated pool from _lastFrac, but
        // that restore is unverified, so the stable 5-bar pool is the default. Flip BossForceNativePhase2 on
        // to test whether the health-restore survives the transition.
        if (_cfg.BossForceNativePhase2 && !_phase2Triggered && _barsConsumed >= Math.Max(1, _cfg.BossHealthBars / 2))
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
            if (patron != null)
                CastNextUlt(patron);
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
            case SimKind.LightningAoe: FireStorm(patron, def.Label); break;
            case SimKind.BarrageAoe: FireBarrage(patron, def.Label); break;
            case SimKind.ChargeBlast: FireChargeBlast(patron); break;
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
        target.EmitSound(_cfg.BossLaserHitSound);
        target.Hurt(CurrentUltDamage, attacker: patron, inflictor: patron);
    }

    /// <summary>Seven's Storm Cloud: an overhead cloud, then staggered lightning strikes scattered across the
    /// ground around the heroes. Real gigawatt particles + sounds; the boss owns the damage.</summary>
    private void FireStorm(CBaseEntity patron, string label)
    {
        var focus = RandomHeroInRange(patron);
        if (focus == null) return;

        Vector3 center = focus.Position;
        Chat.PrintToChatAll($"[Boss Rush] Hidden King casts {label}!");
        patron.EmitSound(_cfg.BossStormCastSound);

        int bolts = Math.Max(1, _cfg.BossStormBolts);
        float cloudLife = bolts * _cfg.BossStormBoltIntervalSeconds + 1f;
        SpawnFx(_cfg.BossStormCloudParticle, center, cloudLife); // cloud lingers over the strike zone

        uint handle = patron.EntityHandle;
        uint focusHandle = focus.EntityHandle;
        float perBolt = CurrentUltDamage * 0.4f;
        for (int i = 0; i < bolts; i++)
        {
            int delayMs = (int)(i * _cfg.BossStormBoltIntervalSeconds * 1000);
            Vector3 scattered = Scatter(center, _cfg.BossBarrageScatter);
            bool isLast = i == bolts - 1;
            _timer.Once(delayMs.Milliseconds(), () =>
            {
                var p = CBaseEntity.FromHandle(handle);
                if (p == null || !p.IsAlive) return;
                // The finale lands directly on the focus hero (so the stun reliably catches someone); the rest scatter.
                Vector3 spot = scattered;
                if (isLast && CBaseEntity.FromHandle(focusHandle) is { IsAlive: true } f) spot = f.Position;
                // Layer bolt + bright strike endcap for energy, and arc a zap onto whoever's caught under it.
                SpawnFx(_cfg.BossStormBoltParticle, spot, _cfg.BossExplodeLifetimeSeconds);
                SpawnFx(_cfg.BossStormStrikeParticle, spot, _cfg.BossExplodeLifetimeSeconds);
                if (NearestHeroTo(spot, _cfg.BossUltAoeRadius) is { } struck)
                    SpawnFx(_cfg.BossStormZapParticle, struck.Position, _cfg.BossExplodeLifetimeSeconds);
                CBaseEntity.FromHandle(focusHandle)?.EmitSound(_cfg.BossStormBoltSound);
                DamageInRadius(p, spot, perBolt);

                // Final strike: root/stun everyone it catches for BossStormFinalStunSeconds.
                if (isLast)
                {
                    float r2 = _cfg.BossUltAoeRadius * _cfg.BossUltAoeRadius;
                    foreach (var hero in LivingHeroes())
                        if (Vector3.DistanceSquared(hero.Position, spot) <= r2)
                            ApplyStun(hero, p, _cfg.BossStormFinalStunSeconds);
                }
            });
        }
    }

    /// <summary>Barrage/bombs: an airstrike carpet — a heading is chosen and a volley of bombs marches along a
    /// line across the target area, step by step (~4s at defaults), like a called-in strafing run.</summary>
    private void FireBarrage(CBaseEntity patron, string label)
    {
        var focus = RandomHeroInRange(patron);
        if (focus == null) return;

        Vector3 center = focus.Position;
        Chat.PrintToChatAll($"[Boss Rush] Hidden King casts {label}!");
        patron.EmitSound(_cfg.BossWarningSound);

        // Random horizontal heading; the carpet starts behind the target and walks forward through it.
        double ang = _rng.NextDouble() * Math.PI * 2;
        var dir = new Vector3((float)Math.Cos(ang), (float)Math.Sin(ang), 0f);
        var perp = new Vector3(-dir.Y, dir.X, 0f);
        Vector3 start = center - dir * (_cfg.BossBarrageLength * 0.5f);

        uint handle = patron.EntityHandle;
        uint focusHandle = focus.EntityHandle;
        int steps = Math.Max(1, _cfg.BossBarrageHits);
        int volley = Math.Max(1, _cfg.BossBarrageVolley);
        float perHit = CurrentUltDamage * 0.35f;
        float stepLen = steps > 1 ? _cfg.BossBarrageLength / (steps - 1) : 0f;

        for (int i = 0; i < steps; i++)
        {
            int delayMs = (int)(i * _cfg.BossBarrageIntervalSeconds * 1000);
            Vector3 line = start + dir * (stepLen * i);
            // Pre-roll this volley's scattered impact points (perpendicular width + slight along-line spread).
            var spots = new Vector3[volley];
            for (int v = 0; v < volley; v++)
            {
                float jit = (float)((_rng.NextDouble() * 2 - 1) * _cfg.BossBarrageScatter);
                float along = (float)((_rng.NextDouble() * 2 - 1) * stepLen * 0.4f);
                spots[v] = line + perp * jit + dir * along;
            }
            _timer.Once(delayMs.Milliseconds(), () =>
            {
                var p = CBaseEntity.FromHandle(handle);
                if (p == null || !p.IsAlive) return;
                float kbR2 = _cfg.BossBombKnockbackRadius * _cfg.BossBombKnockbackRadius;
                foreach (var spot in spots)
                {
                    SpawnFx(_cfg.BossBarrageParticle, spot, _cfg.BossExplodeLifetimeSeconds);
                    DamageInRadius(p, spot, perHit);
                    foreach (var hero in LivingHeroes())
                        if (Vector3.DistanceSquared(hero.Position, spot) <= kbR2)
                            KnockbackFrom(spot, hero, _cfg.BossBombKnockback, _cfg.BossBombKnockbackUp);
                }
                CBaseEntity.FromHandle(focusHandle)?.EmitSound(_cfg.BossImpactSound);
            });
        }
    }

    /// <summary>The boss's native shrine/aoe_wave charge-up explosion, scaled to ~4× area: telegraphs with a charge
    /// particle + warning, then detonates a base-wide blast (core explosion + a ring of shockwaves) that heavily
    /// damages and flings everyone caught — forcing heroes to flee or hide during the charge window.</summary>
    private void FireChargeBlast(CBaseEntity patron)
    {
        var focus = RandomHeroInRange(patron);
        Vector3 center = focus?.Position ?? patron.Position; // chase a hero if one's in range, else blast the base

        Chat.PrintToChatAll("[Boss Rush] The Hidden King charges a devastating blast — RUN!");
        patron.EmitSound(_cfg.BossChargeWarnSound);
        SpawnFx(_cfg.BossChargeChargeParticle, center, _cfg.BossChargeBlastChargeSeconds);

        // Telegraph the danger zone: ground decals around the perimeter so players see how far to run.
        int tele = Math.Max(0, _cfg.BossChargeBlastRing);
        for (int i = 0; i < tele; i++)
        {
            double a = i / (double)tele * Math.PI * 2;
            Vector3 edge = center + new Vector3((float)Math.Cos(a), (float)Math.Sin(a), 0f) * _cfg.BossChargeBlastRadius;
            SpawnFx(_cfg.BossChargeGroundParticle, edge, _cfg.BossChargeBlastChargeSeconds);
        }

        uint handle = patron.EntityHandle;
        _timer.Once(((int)(_cfg.BossChargeBlastChargeSeconds * 1000)).Milliseconds(), () =>
        {
            var p = CBaseEntity.FromHandle(handle);
            if (p == null || !p.IsAlive) return;

            // Visual: core explosion + a ring of shockwaves so the huge area reads, not just a center puff.
            SpawnFx(_cfg.BossChargeExplodeParticle, center, 2f);
            SpawnFx(_cfg.BossChargeWaveParticle, center, 2f);
            int ring = Math.Max(0, _cfg.BossChargeBlastRing);
            for (int i = 0; i < ring; i++)
            {
                double a = i / (double)ring * Math.PI * 2;
                Vector3 edge = center + new Vector3((float)Math.Cos(a), (float)Math.Sin(a), 0f) * (_cfg.BossChargeBlastRadius * 0.6f);
                SpawnFx(_cfg.BossChargeWaveParticle, edge, 2f);
            }
            p.EmitSound(_cfg.BossChargeImpactSound);

            // Damage + heavy knockback to everyone still in the (large) radius.
            float r2 = _cfg.BossChargeBlastRadius * _cfg.BossChargeBlastRadius;
            float dmg = CurrentUltDamage * _cfg.BossChargeBlastDamageMult;
            foreach (var hero in LivingHeroes())
                if (Vector3.DistanceSquared(hero.Position, center) <= r2)
                {
                    hero.Hurt(dmg, attacker: p, inflictor: p);
                    KnockbackFrom(center, hero, _cfg.BossBombKnockback, _cfg.BossBombKnockbackUp);
                }
        });
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

    private void SpawnFx(string particle, Vector3 center, float lifeSec)
    {
        var fx = CParticleSystem.Create(particle).AtPosition(center).Spawn();
        if (fx != null) _timer.Once(Math.Max(100, (int)(lifeSec * 1000)).Milliseconds(), () => fx.Destroy());
    }

    /// <summary>A random point within <paramref name="radius"/> of <paramref name="center"/> on the ground plane.</summary>
    private Vector3 Scatter(Vector3 center, float radius)
    {
        double ang = _rng.NextDouble() * Math.PI * 2;
        float dist = (float)(_rng.NextDouble() * radius);
        return new Vector3(center.X + (float)Math.Cos(ang) * dist,
                           center.Y + (float)Math.Sin(ang) * dist,
                           center.Z);
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

    /// <summary>Shove a hero away from <paramref name="origin"/> (horizontal push + a small upward pop) by setting
    /// its absolute velocity — gives the bombs a couple meters of physical displacement, no modifier needed.</summary>
    private void KnockbackFrom(Vector3 origin, CCitadelPlayerPawn hero, float horiz, float up)
    {
        Vector3 d = hero.Position - origin;
        d.Z = 0f;
        d = d.LengthSquared() < 1f
            ? new Vector3((float)(_rng.NextDouble() * 2 - 1), (float)(_rng.NextDouble() * 2 - 1), 0f)
            : d;
        d = Vector3.Normalize(d);
        hero.AbsVelocity = d * horiz + new Vector3(0f, 0f, up);
    }

    /// <summary>Stun a hero for <paramref name="seconds"/>. Guaranteed effect: pin its velocity to zero (root) for the
    /// duration. If <see cref="BossRushConfig.BossStunModifier"/> is set (and its VData is shipped server-side), also
    /// apply that real no-input CC — same VPK constraint as the Rem sleep, so it's empty/off by default.</summary>
    private void ApplyStun(CCitadelPlayerPawn hero, CBaseEntity patron, float seconds)
    {
        uint h = hero.EntityHandle;
        int durMs = (int)(seconds * 1000);
        var pin = _timer.Every(100.Milliseconds(), () =>
        {
            if (CBaseEntity.FromHandle(h) is { IsAlive: true } e) e.AbsVelocity = Vector3.Zero;
        });
        _timer.Once(durMs.Milliseconds(), () => pin.Cancel());

        // Real CC: the generic knockdown stun (the yellow-ring stun Walkers/heavy-melee apply). Try the primary
        // name, then the fallback subclass name; remove whichever applied when it expires.
        string? applied = TryStunModifier(hero, patron, _cfg.BossStunModifier, seconds)
                       ?? TryStunModifier(hero, patron, _cfg.BossStunModifierFallback, seconds);
        if (applied != null)
            _timer.Once(durMs.Milliseconds(), () => CBaseEntity.FromHandle(h)?.RemoveModifier(applied));
    }

    /// <summary>Apply a stun modifier by name; returns the name on success, null if it didn't take (so the caller
    /// can fall through to the next candidate).</summary>
    private string? TryStunModifier(CCitadelPlayerPawn hero, CBaseEntity patron, string name, float seconds)
    {
        if (string.IsNullOrEmpty(name)) return null;
        using var kv = new KeyValues3();
        kv.SetFloat("duration", seconds);
        return hero.AddModifier(name, kv, caster: patron) != null ? name : null;
    }

    /// <summary>The living hero closest to a world point within <paramref name="radius"/> (null if none).</summary>
    private CCitadelPlayerPawn? NearestHeroTo(Vector3 pos, float radius)
    {
        CCitadelPlayerPawn? best = null;
        float bestDist = radius * radius;
        foreach (var h in LivingHeroes())
        {
            float d = Vector3.DistanceSquared(h.Position, pos);
            if (d <= bestDist) { bestDist = d; best = h; }
        }
        return best;
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

    /// <summary>List the boss's ability ents + their CooldownEnd, and zero them (dev — does it then attack?).</summary>
    public string DebugAbilities()
    {
        var p = FindPatron();
        if (p == null) return "no enemy Patron";
        var parts = new List<string>();
        foreach (var e in Entities.All)
        {
            if (e.TeamNum != p.TeamNum || !e.DesignerName.StartsWith("citadel_ability_tier3boss", StringComparison.OrdinalIgnoreCase)) continue;
            var ab = e.As<CCitadelBaseAbility>();
            parts.Add($"{e.DesignerName.Replace("citadel_ability_tier3boss_", "")}=cdEnd{(ab?.CooldownEnd ?? -1f):F0}");
            if (ab != null) ab.CooldownEnd = 0f;
        }
        return $"clock={GameRules.GameClock:F0}; abilities[{parts.Count}]: {string.Join(" ", parts)} (zeroed)";
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
        if (hp > 0) { patron.MaxHealth = hp; patron.Health = hp; _bossPool = hp; }
        _patronHandle = patron.EntityHandle;
        _barsConsumed = 0;
        _phase2Triggered = false;
        _lastFrac = 1f;
        return true;
    }
}
