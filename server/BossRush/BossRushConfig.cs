namespace BossRush;

/// <summary>
/// All gameplay tunables for Boss Rush, surfaced via Deadworks' <c>[PluginConfig]</c> so they
/// can be tweaked without a recompile. Values are starting points to balance during P1–P5.
/// </summary>
public sealed class BossRushConfig
{
    // ── Spawning (DESIGN.md #9, #10) ──────────────────────────────────────────────
    /// <summary>
    /// Hidden King lane indices for <c>citadel_spawn_trooper_zipline &lt;team&gt; &lt;lane&gt;</c>.
    /// dl_midtown (confirmed in-game): 1 = left, 4 = middle, 6 = right.
    /// </summary>
    public string HiddenKingLanesCsv { get; set; } = "1,4,6";

    /// <summary>Seconds between baseline Hidden King reinforcement spawns (~ the natural wave cadence).</summary>
    public float ReinforcementIntervalSeconds { get; set; } = 30.0f;

    /// <summary>Extra Hidden King troopers spawned on EACH lane per reinforcement — keeps the Hidden King ahead of the Archmother (~2×).</summary>
    public int ReinforcementSquadSize { get; set; } = 4;

    /// <summary>Enemy troopers start this much stronger than baseline (health), scaling with match time.</summary>
    public float DenizenBaseStrengthMultiplier { get; set; } = 2.0f;

    /// <summary>Extra denizen strength added per minute of match time (linear scaling).</summary>
    public float DenizenStrengthPerMinute { get; set; } = 0.04f;

    // ── Health regen (custom heal loop; the HUD regen stat can't be set directly) ──
    /// <summary>HP/sec the heroes regen at match start.</summary>
    public float RegenStartPerSecond { get; set; } = 50.0f;
    /// <summary>HP/sec the heroes regen once fully ramped.</summary>
    public float RegenMaxPerSecond { get; set; } = 200.0f;
    /// <summary>Minutes over which regen ramps linearly from start to max.</summary>
    public float RegenRampMinutes { get; set; } = 30.0f;
    /// <summary>Regen pauses this many seconds after a hero takes damage.</summary>
    public float RegenWaitSeconds { get; set; } = 5.0f;

    // ── Rage waves (DESIGN.md #12) ────────────────────────────────────────────────
    /// <summary>Minutes between automatic rage waves.</summary>
    public float RageWaveIntervalMinutes { get; set; } = 10.0f;
    /// <summary>How long a surge lasts before the normal cadence restores.</summary>
    public float RageWaveSurgeDurationSeconds { get; set; } = 60.0f;
    /// <summary>Trooper spawn interval (seconds) during a surge — lower = faster waves.</summary>
    public float RageWaveSpawnIntervalSeconds { get; set; } = 20.0f;
    /// <summary>Troopers per squad at match start.</summary>
    public int RageWaveSquadBase { get; set; } = 10;
    /// <summary>Squad grows by this much at the first step, and again every step interval after.</summary>
    public int RageWaveSquadStep { get; set; } = 5;
    /// <summary>First squad bump happens at this match minute (e.g. 10 → 15 at 20 min).</summary>
    public float RageWaveSquadFirstStepMinute { get; set; } = 20.0f;
    /// <summary>After the first bump, squad grows again every this many minutes (+5 / 10 min).</summary>
    public float RageWaveSquadStepMinutes { get; set; } = 10.0f;
    /// <summary>Soundevent fired on each client when a rage wave begins (ships in the client addon).</summary>
    public string RageWaveStartSound { get; set; } = "bossrush.ragewave.start";

    // ── Patron combat (DESIGN.md #2, §4) ──────────────────────────────────────────
    public float PatronLaserBaseDamage { get; set; } = 60.0f;
    /// <summary>Added laser/ult damage per minute — the Hidden King gets scarier as the match drags on.</summary>
    public float PatronLaserDamagePerMinute { get; set; } = 8.0f;
    public string PatronLaserParticle { get; set; } = "particles/bossrush/patron_laser.vpcf";
    public string PatronLaserSound { get; set; } = "bossrush.patron.laser";
    /// <summary>How often the Hidden King rolls a random self-buff modifier.</summary>
    public float PatronBuffRollIntervalSeconds { get; set; } = 30.0f;

    // ── Hidden King boss — multi-phase finale (DESIGN.md §4) ──────────────────────
    /// <summary>Health bars to split the Patron's pool into; each lost bar escalates the fight.</summary>
    public int BossHealthBars { get; set; } = 5;
    /// <summary>Total Patron health to set on spawn (sizes the bars). 0 = keep its native health.</summary>
    public int BossMaxHealth { get; set; } = 0;
    /// <summary>How often (seconds) to poll the King's health for a bar transition.</summary>
    public float BossPhasePollSeconds { get; set; } = 0.5f;
    /// <summary>Seconds between the King's attacks at full health (cadence on the first bar).</summary>
    public float BossAttackIntervalSeconds { get; set; } = 6.0f;
    /// <summary>Seconds between attacks on the last bar — cadence shrinks to this as bars fall.</summary>
    public float BossAttackIntervalMinSeconds { get; set; } = 1.5f;
    /// <summary>Radius (units) of the King's AoE ults (lightning / barrage).</summary>
    public float BossUltAoeRadius { get; set; } = 600.0f;
    /// <summary>Extra ult damage added per health bar already lost.</summary>
    public float BossUltDamagePerBar { get; set; } = 30.0f;
    /// <summary>Max distance (units) a hero can be from the King and still be attacked — stops whole-map hits.</summary>
    public float BossEngageRange { get; set; } = 3000.0f;
    /// <summary>Fire the boss's REAL abilities via its citadel_boss_tier_3_test_* cvars (real VFX, LoS, durations) instead of simulated effects.</summary>
    public bool BossUseNativeAbilities { get; set; } = true;
    /// <summary>After firing a native ability cvar, reset it to 0 after this long so the next trigger re-fires (0 = never reset).</summary>
    public float BossNativeResetSeconds { get; set; } = 1.5f;
    /// <summary>Hits per simulated barrage ult (fallback only), staggered over time (~10s at defaults).</summary>
    public int BossBarrageHits { get; set; } = 12;
    /// <summary>Seconds between simulated barrage hits.</summary>
    public float BossBarrageIntervalSeconds { get; set; } = 0.8f;
    /// <summary>The "Rem — Naptime" sleep CC. Rem's internal codename is "familiar"; CCitadel_Modifier_Familiar_Asleep : CCitadel_Modifier_Sleep (verified from the game schema dump).</summary>
    public string BossSleepModifier { get; set; } = "modifier_familiar_asleep";
    /// <summary>How long the sleep lasts (Naptime is ~4s).</summary>
    public float BossSleepDurationSeconds { get; set; } = 4.0f;
    /// <summary>Actually apply the sleep modifier. OFF until modifier_familiar_asleep's VData is loaded server-side via a VPK (P4) — applying an unregistered modifier just logs "VData not found". Flip on once the addon ships it.</summary>
    public bool BossApplySleepModifier { get; set; } = false;

    // ── Economy / items (DESIGN.md #4, #5, #6, #8) ────────────────────────────────
    /// <summary>Upgrade Station charges this multiple of an item's normal shop price to enhance it.</summary>
    public float UpgradeCostMultiplier { get; set; } = 2.0f;
    /// <summary>Chance (0–1) that a world drop rolls an *enhanced* item instead of a base one. Keep tiny.</summary>
    public float EnhancedDropChance { get; set; } = 0.01f;
    /// <summary>Timed enhancements expire after this long unless the player dies first.</summary>
    public float EnhancementDurationSeconds { get; set; } = 300.0f; // 5 minutes

    // ── Items beyond the 12 visible slots (DESIGN.md #7, §3) ──────────────────────
    // No cap to configure: under the Street Brawl ruleset, AddItem keeps granting items past the
    // 12 visible slots (they stay equipped, just hidden). Showing them is an optional client HUD.

    // ── Objective (DESIGN.md #1, #3) ──────────────────────────────────────────────
    /// <summary>How many Guardians to run per lane (vanilla is 1× — this doubles defenses).</summary>
    public int GuardiansPerLaneMultiplier { get; set; } = 2;
}
