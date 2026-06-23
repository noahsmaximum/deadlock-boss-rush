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
    /// <summary>Rage-wave soundevent. Stock MidBoss.Arrive (Base.MapObjective = global/2D, ~10s dramatic stinger) from gameplay.vsndevts; no addon needed.</summary>
    public string RageWaveStartSound { get; set; } = "MidBoss.Arrive";

    // ── Patron combat (DESIGN.md #2, §4) ──────────────────────────────────────────
    public float PatronLaserBaseDamage { get; set; } = 130.0f;
    /// <summary>Added laser/ult damage per minute — the Hidden King gets scarier as the match drags on.</summary>
    public float PatronLaserDamagePerMinute { get; set; } = 12.0f;
    // Real shipped boss assets (decompiled from citadel_ability_tier3boss_* in the live abilities.vdata) so
    // the scripted-ult sim path actually renders + sounds. Must be precached on map load (OnPrecacheResources).
    /// <summary>Beam particle for the laser ult (boss's own tier3 beam).</summary>
    public string PatronLaserParticle { get; set; } = "particles/npc/tier3boss/tier3_boss_beam.vpcf";
    /// <summary>Fire sound when the laser ult goes off.</summary>
    public string PatronLaserSound { get; set; } = "Ability.Tier2Boss.LaserBeam.Fire";
    /// <summary>Hit sound played on the laser's target.</summary>
    public string BossLaserHitSound { get; set; } = "Guardian.T2.Beam.Hit";
    /// <summary>Explosion particle for AoE / bomb / barrage impacts (the Patron's real bomb explosion).</summary>
    public string BossExplodeParticle { get; set; } = "particles/npc/patron/patron_bomb_explode.vpcf";
    /// <summary>Impact sound at each AoE / bomb / barrage detonation.</summary>
    public string BossImpactSound { get; set; } = "Patron.King.AOE.Impact";
    /// <summary>Telegraph sound when an AoE / barrage ult begins.</summary>
    public string BossWarningSound { get; set; } = "Patron.King.AOE.Warning";
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
    /// <summary>Seconds between attacks on the last bar — cadence shrinks to this as bars fall (raised so the final bar isn't a non-stop barrage).</summary>
    public float BossAttackIntervalMinSeconds { get; set; } = 2.5f;
    /// <summary>Radius (units) of the King's AoE ults (lightning / barrage).</summary>
    public float BossUltAoeRadius { get; set; } = 600.0f;
    /// <summary>Extra ult damage added per health bar already lost.</summary>
    public float BossUltDamagePerBar { get; set; } = 40.0f;
    /// <summary>Max distance (units) a hero can be from the King and still be attacked — stops whole-map hits.</summary>
    public float BossEngageRange { get; set; } = 3000.0f;
    /// <summary>Fire the boss's REAL abilities via its citadel_boss_tier_3_test_* cvars instead of the simulated
    /// effects. OFF by default: the cvars are AI-gated (fire unreliably) and short-circuit the sim, so the
    /// scripted rotation showed nothing. With this off, the rotation uses the real-asset sim path (reliable VFX
    /// + damage + sound) while the boss's own AI still attacks organically. dw_br_bossfire still forces cvars.</summary>
    public bool BossUseNativeAbilities { get; set; } = false;
    /// <summary>Force the native Phase 2 at the midpoint. OFF by default: the native transition resets the boss to its small native health pool (~12k) and wipes the inflated bar pool. PollPhase restores the pool from the last fraction, but that's unverified — flip on to test it.</summary>
    public bool BossForceNativePhase2 { get; set; } = false;
    /// <summary>Roll a random Patron self-buff every PatronBuffRollIntervalSeconds. OFF: the buff names are placeholders with no VData, so it just spams "VData not found". Flip on once real self-buff modifiers exist.</summary>
    public bool BossSelfBuffsEnabled { get; set; } = false;
    /// <summary>After firing a native ability cvar, reset it to 0 after this long so the next trigger re-fires (0 = never reset).</summary>
    public float BossNativeResetSeconds { get; set; } = 1.5f;
    /// <summary>Steps in the bomb run — each step drops a volley, marching along a line (airstrike carpet). 5 steps ≈ 4s.</summary>
    public int BossBarrageHits { get; set; } = 5;
    /// <summary>Seconds between bomb-run steps (between each volley).</summary>
    public float BossBarrageIntervalSeconds { get; set; } = 0.7f;
    /// <summary>Bombs dropped per step (the volley) — they land together with perpendicular jitter, like a strafing run.</summary>
    public int BossBarrageVolley { get; set; } = 3;
    /// <summary>Length (units) of the bomb run — the carpet marches this far across the target area.</summary>
    public float BossBarrageLength { get; set; } = 1100.0f;
    /// <summary>Perpendicular jitter (units) on each bomb within a volley — the width of the carpet.</summary>
    public float BossBarrageScatter { get; set; } = 220.0f;
    /// <summary>Smaller per-explosion particle for the bomb run (rocket-explosion, not the giant Patron bomb).</summary>
    public string BossBarrageParticle { get; set; } = "particles/npc/tier2boss/tier2boss_barrage_explosion_ground.vpcf";
    /// <summary>Seconds each barrage/bomb explosion particle lingers before cleanup (shorter = less screen clutter).</summary>
    public float BossExplodeLifetimeSeconds { get; set; } = 1.0f;
    /// <summary>Horizontal knockback speed (units/s) applied to a hero caught in a bomb explosion — tuned for ~5-6m of displacement.</summary>
    public float BossBombKnockback { get; set; } = 900.0f;
    /// <summary>Upward knockback speed (units/s) on a bomb hit so the push carries far (a real pop off the ground).</summary>
    public float BossBombKnockbackUp { get; set; } = 480.0f;
    /// <summary>Only heroes within this radius (units) of a bomb explosion get displaced (tighter than the damage radius).</summary>
    public float BossBombKnockbackRadius { get; set; } = 320.0f;

    // ── Seven — Storm Cloud ult (real gigawatt assets) ────────────────────────────
    /// <summary>The overhead storm-cloud particle (Seven's ult, enemy-cast variant).</summary>
    public string BossStormCloudParticle { get; set; } = "particles/abilities/gigawatt/gigawatt_storm_cloud_cast_enemy.vpcf";
    /// <summary>The lightning-strike particle for each storm bolt.</summary>
    public string BossStormBoltParticle { get; set; } = "particles/abilities/gigawatt/gigawatt_storm_cloud_ground_bolt.vpcf";
    /// <summary>Bright strike-impact endcap layered on each bolt for extra energy/flash.</summary>
    public string BossStormStrikeParticle { get; set; } = "particles/abilities/gigawatt/gigawatt_storm_cloud_ground_bolt_endcap.vpcf";
    /// <summary>Direct zap particle attached to a struck hero (the bolt connecting to them).</summary>
    public string BossStormZapParticle { get; set; } = "particles/abilities/gigawatt/gigawatt_storm_cloud_bolt_enemy.vpcf";
    /// <summary>Sound when the storm cloud forms.</summary>
    public string BossStormCastSound { get; set; } = "Gigawatt.StormCloud.Cast";
    /// <summary>Sound for each lightning strike.</summary>
    public string BossStormBoltSound { get; set; } = "Gigawatt.StormCloud.Bolt.Detonate";
    // ── Hidden King — Bomb Blast (the boss's native drop_bombs charge-up explosion, scaled ~4× area) ──
    // This is the blast from `dw_br_bossfire bomb`: a charge-up telegraph then a big Patron bomb explosion.
    /// <summary>Blast radius (units) of the charged explosion, scaled up (~4× area) so heroes must flee the base or hide.</summary>
    public float BossChargeBlastRadius { get; set; } = 1400.0f;
    /// <summary>Charge-up telegraph (seconds) before the blast detonates — the window to run/hide.</summary>
    public float BossChargeBlastChargeSeconds { get; set; } = 3.0f;
    /// <summary>Charge damage = ult damage × this (a heavy hit for those who don't escape).</summary>
    public float BossChargeBlastDamageMult { get; set; } = 1.5f;
    /// <summary>Extra bomb explosions arranged around the perimeter so the (huge) blast area reads visually.</summary>
    public int BossChargeBlastRing { get; set; } = 10;
    /// <summary>Telegraph particle during the charge-up (Patron AoE charge).</summary>
    public string BossChargeChargeParticle { get; set; } = "particles/npc/patron/patron_aoe_chargeup.vpcf";
    /// <summary>Ground danger-zone decal shown around the perimeter during the charge (so players see where to run).</summary>
    public string BossChargeGroundParticle { get; set; } = "particles/npc/patron/patron_bomb_counter_amber.vpcf";
    /// <summary>Core explosion particle on detonation (Patron bomb explosion).</summary>
    public string BossChargeExplodeParticle { get; set; } = "particles/npc/patron/patron_bomb_explode.vpcf";
    /// <summary>Explosion particle used to fill the perimeter ring on detonation.</summary>
    public string BossChargeWaveParticle { get; set; } = "particles/npc/patron/patron_bomb_explode.vpcf";
    /// <summary>Warning sound during the charge-up.</summary>
    public string BossChargeWarnSound { get; set; } = "Patron.King.AOE.Warning";
    /// <summary>Impact sound on detonation.</summary>
    public string BossChargeImpactSound { get; set; } = "Patron.King.AOE.Impact";

    /// <summary>Lightning strikes per storm cloud, staggered over time.</summary>
    public int BossStormBolts { get; set; } = 9;
    /// <summary>Seconds between storm lightning strikes (fast = energetic).</summary>
    public float BossStormBoltIntervalSeconds { get; set; } = 0.45f;
    /// <summary>The final storm bolt lands on the focus hero and roots/stuns everyone it catches for this long.</summary>
    public float BossStormFinalStunSeconds { get; set; } = 2.0f;
    /// <summary>Stun modifier for the final bolt — the generic knockdown stun (the yellow-ring CC a Walker stomp / heavy
    /// melee applies). It's a base citadel modifier used by citadel_ability_hold_melee, so it's loaded server-side
    /// (unlike the hero-specific Rem sleep). ApplyStun also tries BossStunModifierFallback if this one fails to apply.
    /// The velocity root still runs as a guaranteed fallback regardless.</summary>
    public string BossStunModifier { get; set; } = "modifier_citadel_knockdown";
    /// <summary>Secondary stun-modifier name tried if BossStunModifier doesn't apply (the data subclass name).</summary>
    public string BossStunModifierFallback { get; set; } = "modifier_stun";
    /// <summary>The "Rem — Naptime" sleep CC. Rem's internal codename is "familiar"; CCitadel_Modifier_Familiar_Asleep : CCitadel_Modifier_Sleep (verified from the game schema dump).</summary>
    public string BossSleepModifier { get; set; } = "modifier_familiar_asleep";
    /// <summary>How long the sleep lasts (Naptime is ~4s).</summary>
    public float BossSleepDurationSeconds { get; set; } = 4.0f;
    /// <summary>Actually apply the sleep modifier. OFF until modifier_familiar_asleep's VData is loaded server-side via a VPK (P4) — applying an unregistered modifier just logs "VData not found". Flip on once the addon ships it.</summary>
    public bool BossApplySleepModifier { get; set; } = false;

    // ── Economy / items (DESIGN.md #4, #5, #6, #8) ────────────────────────────────
    /// <summary>Upgrade Station charges this multiple of an item's normal shop price to enhance it.</summary>
    public float UpgradeCostMultiplier { get; set; } = 2.0f;
    /// <summary>Base shop price per tier [index 0 unused, then T1..T5] — mirrors m_nItemPricePerTier.</summary>
    public int[] ItemTierPrices { get; set; } = { 0, 800, 1600, 3200, 6400, 9999 };
    /// <summary>Flat soul cost to buy a legendary (top-tier) item from the store.</summary>
    public int LegendaryPrice { get; set; } = 30000;
    /// <summary>Which tier counts as "legendary" — buyable at the store; everything below is loot-only.</summary>
    public int LegendaryTier { get; set; } = 5;
    /// <summary>The store sells ONLY legendaries (T5): clicking any other buyable card is blocked (power comes from
    /// world loot, not the shop). Turn off to let base items be bought through the native flow again.</summary>
    public bool StoreLegendariesOnly { get; set; } = true;
    // NOTE: a server-side BlockNativePurchases flag was removed — OnModifyCurrency.Stop can't veto a purchase
    // (verified in-game). Buy-gating lives in the client shop UI instead (hide un-owned items).
    /// <summary>Chance (0–1) that a world drop rolls an *enhanced* item instead of a base one. Keep tiny.</summary>
    public float EnhancedDropChance { get; set; } = 0.01f;
    /// <summary>Enhancements are permanent (no timer, survive death). Flip off to use the timed behavior below.</summary>
    public bool EnhancementPermanent { get; set; } = true;
    /// <summary>If not permanent, timed enhancements expire after this long unless the player dies first.</summary>
    public float EnhancementDurationSeconds { get; set; } = 300.0f; // 5 minutes

    // ── World loot (DESIGN.md #4, #6 — power from the world) ──────────────────────
    /// <summary>Master switch: breaking world containers (boxes / golden buddhas) can drop items.</summary>
    public bool LootEnabled { get; set; } = true;
    /// <summary>The entities a broken container drops — detected on SPAWN (damage/touch hooks don't fire for props),
    /// so a box breaking by any means is caught when its drop appears. Most boxes drop a modifier pickup, fewer drop
    /// gold; listening on both covers nearly every break. Loot goes to the nearest hero.</summary>
    public string[] LootPickupDesignerNames { get; set; } =
    {
        "citadel_breakable_prop_gold_pickup",
        "citadel_breakable_prop_modifier_pickup",
    };
    /// <summary>Max distance (units) from a broken container to attribute its loot to a hero (generous — breaks land
    /// ~90–130u away on melee/slide, up to ~900u on ranged shots).</summary>
    public float LootNearRadius { get; set; } = 1500.0f;
    /// <summary>Chance (0–1) that breaking a container drops an item. Each container gives exactly one roll.
    /// Tuned up for a full 6-player team — everyone needs to be finding gear.</summary>
    public float LootDropChance { get; set; } = 0.6f;
    /// <summary>Tell the looter in chat what they found.</summary>
    public bool LootAnnounce { get; set; } = true;

    // ── Loot rarity by match time — relative weights per tier [T1,T2,T3,T4,T5=legendary] ──
    /// <summary>Minutes: end of the early bracket.</summary>
    public float LootTierBracket1Minutes { get; set; } = 5f;
    /// <summary>Minutes: end of the mid bracket (after this is the late bracket).</summary>
    public float LootTierBracket2Minutes { get; set; } = 10f;
    /// <summary>0–5 min: mostly T1, T2 rare.</summary>
    public float[] LootTierWeightsEarly { get; set; } = { 85, 15, 0, 0, 0 };
    /// <summary>5–10 min: T1 &amp; T2 mostly, T3 rare.</summary>
    public float[] LootTierWeightsMid { get; set; } = { 45, 45, 10, 0, 0 };
    /// <summary>10+ min: T2/T3 common, T1/T4 rare, legendaries (T5) insanely rare.</summary>
    public float[] LootTierWeightsLate { get; set; } = { 10, 38, 40, 11, 1 };

    // ── Front-load loot — spawn crates at match start via the game's crate convars ──
    /// <summary>Spawn loot crates at match start instead of after the default delay (bridge-buff powerup spawners
    /// are separate and left alone). Zeros the citadel_crate_* spawn-delay convars in ApplyRuleset.</summary>
    public bool FrontloadCrates { get; set; } = true;
    /// <summary>citadel_crate_respawn_interval (seconds) — how fast broken crates return. -1 = leave the game default (~360).</summary>
    public int CrateRespawnIntervalSeconds { get; set; } = 120;
    /// <summary>citadel_breakable_prop_spawn_interval_override (seconds) — how fast broken world breakables (the bulk
    /// loot source) repopulate, so the mid-game isn't a drought. -1 = leave the game default.</summary>
    public int BreakableRespawnSeconds { get; set; } = 90;

    // ── Items beyond the 12 visible slots (DESIGN.md #7, §3) ──────────────────────
    // No cap to configure: under the Street Brawl ruleset, AddItem keeps granting items past the
    // 12 visible slots (they stay equipped, just hidden). Showing them is an optional client HUD.

    // ── Objective (DESIGN.md #1, #3) ──────────────────────────────────────────────
    /// <summary>How many Guardians to run per lane (vanilla is 1× — this doubles defenses).</summary>
    public int GuardiansPerLaneMultiplier { get; set; } = 2;

    // ── Lane defenses — Walkers (npc_boss_tier2) and Guardians (npc_barrack_boss) ──
    /// <summary>Multiply each enemy Walker's health on spawn so they're a lot tougher to grind through.</summary>
    public float WalkerHealthMultiplier { get; set; } = 4.0f;
    /// <summary>Designer name of the Walkers (the big lane mechs) — buffed via WalkerHealthMultiplier.</summary>
    public string WalkerDesignerName { get; set; } = "npc_boss_tier2";
    /// <summary>Front Guardian HP multiplier (npc_trooper_boss) — the server-side stand-in for "2 guardians":
    /// each front guardian (the first one you hit) is this much tankier. 2 ≈ double the time-to-kill.</summary>
    public float FrontGuardianHealthMultiplier { get; set; } = 2.0f;
    /// <summary>Base-side Guardian HP multiplier (npc_barrack_boss, the deep ones). 1 = untouched.</summary>
    public float BaseGuardianHealthMultiplier { get; set; } = 1.0f;
    /// <summary>Designer name of the base-side Guardians.</summary>
    public string BaseGuardianDesignerName { get; set; } = "npc_barrack_boss";
    /// <summary>Reinforce the lane Guardians on map start. OFF: raw-spawning objective NPCs (npc_trooper_boss) via
    /// CreateByDesignerName+Spawn CRASHES the server (same as troopers) — adding a real 2nd guardian needs a map
    /// edit (CSDK addon), not a runtime spawn. Left here for when that map-edit path lands.</summary>
    public bool DoubleGuardians { get; set; } = false;
    /// <summary>The front Guardian a player encounters first — CONFIRMED npc_trooper_boss (the most-forward enemy
    /// defender, one per lane) from a live dl_midtown dump. Extra ones are placed at ExtraGuardianPositions.</summary>
    public string FrontGuardianDesignerName { get; set; } = "npc_trooper_boss";
    /// <summary>Exact world positions "x,y,z" to spawn one extra front Guardian each (a second guardian next to an
    /// existing one). The middle lane is intentionally left untouched. Health/team copied from a live front Guardian.</summary>
    public string[] ExtraGuardianPositions { get; set; } =
    {
        "7303.78,-1086.22,364.38",
        "599,-1831.19,496.44",
    };
    /// <summary>Seconds after match start to run the guardian-reinforcement sweep (lets map-placed guardians settle).</summary>
    public float GuardianDoubleDelaySeconds { get; set; } = 10.0f;
    /// <summary>How far (units) to offset each guardian twin from its original.</summary>
    public float GuardianTwinOffset { get; set; } = 180.0f;
}
