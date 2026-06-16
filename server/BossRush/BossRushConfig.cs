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
    public float RageWaveIntervalMinutes { get; set; } = 10.0f;
    public float RageWaveTrooperMultiplier { get; set; } = 4.0f;
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

    // ── Relics / beyond-16 power (DESIGN.md #7, §3) ───────────────────────────────
    /// <summary>Number of real Deadlock item slots before extra picks become modifier-based "Relics".</summary>
    public int RealItemSlotCap { get; set; } = 16;

    // ── Objective (DESIGN.md #1, #3) ──────────────────────────────────────────────
    /// <summary>How many Guardians to run per lane (vanilla is 1× — this doubles defenses).</summary>
    public int GuardiansPerLaneMultiplier { get; set; } = 2;
}
