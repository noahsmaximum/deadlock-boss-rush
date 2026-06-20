using System.Numerics;
using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md #4 &amp; #6 — power comes from the world, not the shop. Breaking world containers (boxes,
/// crates, the "golden buddhas") has a chance to drop an item; a tiny fraction roll the temporary
/// *enhanced* variant. Souls/power-ups stay native.
///
/// Breakable props don't route through the OnTakeDamage or OnEntityStartTouch hooks (verified live — neither
/// fires), so we detect the break by its RESULT: when a container is destroyed it spawns a gold pickup, and
/// that spawn fires <see cref="OnEntitySpawned"/>. We roll loot once per pickup and award it to the nearest
/// hero (the breaker is right there for slide/melee; within range for shots). Items are uncapped under Street
/// Brawl, so <c>AddItem</c> keeps working past the 12 visible slots (DESIGN.md §3).
/// </summary>
public sealed class LootSystem
{
    private readonly BossRushConfig _cfg;
    private readonly EnhancementSystem _enhancements;
    private readonly ITimer _timer;
    private readonly Random _rng = new();
    private const int Tiers = 5; // EModTier_1..5 (T5 = legendary)

    private readonly HashSet<uint> _looted = new();   // pickup handles already consumed
    private readonly HashSet<string> _pickups;        // drop entity names that trigger loot
    private readonly List<string>[] _byTier;          // item names bucketed by tier (index 0 = T1)

    public LootSystem(BossRushConfig cfg, EnhancementSystem enhancements, ITimer timer)
    {
        _cfg = cfg;
        _enhancements = enhancements;
        _timer = timer;
        _pickups = new HashSet<string>(cfg.LootPickupDesignerNames, StringComparer.OrdinalIgnoreCase);
        _byTier = LoadTiers();
    }

    public int PoolSize => _byTier.Sum(b => b.Count);
    public string TierSummary => string.Join(" ", _byTier.Select((b, i) => $"T{i + 1}={b.Count}"));

    /// <summary>Bucket every item by tier from all_items_tiers.txt ("upgrade_name,tier" per line — regenerate from
    /// the live abilities.vdata via m_iItemTier). Falls back to the flat all_items.txt in T1 so loot still works.</summary>
    private static List<string>[] LoadTiers()
    {
        var buckets = new List<string>[Tiers];
        for (int i = 0; i < Tiers; i++) buckets[i] = new List<string>();

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "deadlock_dumps");
        var tierPath = Path.Combine(dir, "all_items_tiers.txt");
        if (File.Exists(tierPath))
            foreach (var raw in File.ReadAllLines(tierPath))
            {
                var parts = raw.Split(',');
                if (parts.Length != 2) continue;
                var name = parts[0].Trim();
                if (name.Length > 0 && int.TryParse(parts[1].Trim(), out var t) && t >= 1 && t <= Tiers)
                    buckets[t - 1].Add(name);
            }

        if (buckets.All(b => b.Count == 0)) // no tier file → flat fallback in T1
        {
            var flat = Path.Combine(dir, "all_items.txt");
            if (File.Exists(flat))
                foreach (var raw in File.ReadAllLines(flat))
                {
                    var n = raw.Trim();
                    if (n.Length > 0 && !n.StartsWith("//")) buckets[0].Add(n);
                }
            if (buckets[0].Count == 0) buckets[0].Add("upgrade_sprint_booster");
        }
        return buckets;
    }

    /// <summary>A container's gold pickup just spawned (= a box was broken) — roll one loot drop for the nearest
    /// hero. Fed from the plugin's OnEntitySpawned.</summary>
    public void OnEntitySpawned(EntitySpawnedEvent e)
    {
        if (!_cfg.LootEnabled) return;
        if (!_pickups.Contains(e.Entity.DesignerName)) return;

        uint handle = e.Entity.EntityHandle;
        _timer.NextTick(() => // position settles a tick after spawn
        {
            var pickup = CBaseEntity.FromHandle(handle);
            if (pickup == null || !_looted.Add(handle)) return;

            var pawn = NearestHero(pickup.Position, _cfg.LootNearRadius);
            if (pawn == null) return;                              // no hero near the break — no loot
            if (_rng.NextDouble() > _cfg.LootDropChance) return;   // dud
            GrantLoot(pawn);
        });
    }

    /// <summary>Grant one item — its tier rolled by the current time bracket's weights — rarely as the temporary
    /// enhanced variant, and tell the looter.</summary>
    public void GrantLoot(CCitadelPlayerPawn pawn)
    {
        var itemName = PickItem();
        if (itemName == null) return;
        bool enhanced = _rng.NextDouble() < _cfg.EnhancedDropChance;
        if (enhanced)
            _enhancements.GrantTemporaryEnhanced(pawn, itemName); // rare, temporary
        else
            pawn.AddItem(itemName); // permanent base item; uncapped under Street Brawl rules

        if (_cfg.LootAnnounce && pawn.Controller is { } c)
            Chat.PrintToChat(c, $"[Boss Rush] Looted {(enhanced ? "ENHANCED " : "")}{Pretty(itemName)}!");
    }

    /// <summary>The nearest living hero to a point within <paramref name="radius"/> (the breaker, in practice).</summary>
    private static CCitadelPlayerPawn? NearestHero(Vector3 pos, float radius)
    {
        CCitadelPlayerPawn? best = null;
        float bestSq = radius * radius;
        foreach (var p in Players.GetAllPawns())
        {
            if (p.TeamNum != BossRushPlugin.HeroTeam || p.Health <= 0) continue;
            float d = Vector3.DistanceSquared(p.Position, pos);
            if (d < bestSq) { bestSq = d; best = p; }
        }
        return best;
    }

    /// <summary>Roll a tier by the current time bracket's weights, then a random item from that tier.</summary>
    private string? PickItem()
    {
        int tier = RollTier(CurrentTierWeights());
        // Use the rolled tier; if it has no items, walk down to lower tiers, then up.
        for (int t = tier; t >= 0; t--)
            if (_byTier[t].Count > 0) return _byTier[t][_rng.Next(_byTier[t].Count)];
        for (int t = tier + 1; t < Tiers; t++)
            if (_byTier[t].Count > 0) return _byTier[t][_rng.Next(_byTier[t].Count)];
        return null;
    }

    private float[] CurrentTierWeights()
    {
        float minutes = GameRules.GameClock / 60f;
        if (minutes < _cfg.LootTierBracket1Minutes) return _cfg.LootTierWeightsEarly;
        if (minutes < _cfg.LootTierBracket2Minutes) return _cfg.LootTierWeightsMid;
        return _cfg.LootTierWeightsLate;
    }

    /// <summary>Weighted index into a tier-weights array (0 = T1 … 4 = T5). Returns 0 if all weights are zero.</summary>
    private int RollTier(float[] weights)
    {
        float total = 0f;
        for (int i = 0; i < weights.Length && i < Tiers; i++) total += MathF.Max(0f, weights[i]);
        if (total <= 0f) return 0;

        double r = _rng.NextDouble() * total;
        for (int i = 0; i < weights.Length && i < Tiers; i++)
        {
            r -= MathF.Max(0f, weights[i]);
            if (r < 0) return i;
        }
        return Math.Min(weights.Length, Tiers) - 1;
    }

    private static string Pretty(string upgradeName)
    {
        var s = upgradeName.StartsWith("upgrade_", StringComparison.OrdinalIgnoreCase)
            ? upgradeName["upgrade_".Length..]
            : upgradeName;
        return s.Replace('_', ' ');
    }

    /// <summary>Touch hook is wired but unused — breakable props don't fire it (verified live).</summary>
    public void OnEntityTouch(EntityTouchEvent e) { }
}
