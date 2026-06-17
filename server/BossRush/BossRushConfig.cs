namespace BossRush;

/// <summary>
/// All gameplay tunables for Boss Rush, surfaced via Deadworks' <c>[PluginConfig]</c> so they
/// can be tweaked without a recompile. Values are starting points to balance during P1–P5.
/// </summary>
public sealed class BossRushConfig
{
    // ── Spawning (DESIGN.md #9, #10) ──────────────────────────────────────────────
    /// <summary>Enemy trooper waves spawn this many times relative to normal (heroes' side stays 1×).</summary>
    public float EnemyTrooperSpawnMultiplier { get; set; } = 2.0f;

    /// <summary>Denizens (neutrals) start this much stronger than baseline.</summary>
    public float DenizenBaseStrengthMultiplier { get; set; } = 1.5f;

    /// <summary>Extra denizen strength added per minute of match time (linear scaling).</summary>
    public float DenizenStrengthPerMinute { get; set; } = 0.04f;

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

    // ── Patron combat (DESIGN.md #2) ──────────────────────────────────────────────
    public float PatronLaserBaseDamage { get; set; } = 60.0f;
    /// <summary>Added laser damage per minute — the Patron gets scarier as the match drags on.</summary>
    public float PatronLaserDamagePerMinute { get; set; } = 8.0f;
    public float PatronLaserIntervalSeconds { get; set; } = 3.5f;
    public string PatronLaserParticle { get; set; } = "particles/bossrush/patron_laser.vpcf";
    public string PatronLaserSound { get; set; } = "bossrush.patron.laser";
    /// <summary>How often the Patron rolls a random self-buff modifier.</summary>
    public float PatronBuffRollIntervalSeconds { get; set; } = 30.0f;

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
