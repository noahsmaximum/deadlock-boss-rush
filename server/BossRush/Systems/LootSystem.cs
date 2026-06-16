using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md #4 &amp; #6 — power comes from the world, not the shop. Breaking/touching world
/// containers (gold statues, crates — the "golden buddhas") grants a base item; a tiny fraction
/// roll a (temporary) *enhanced* item instead. Souls and permanent power-ups stay native.
///
/// Items are uncapped under the Street Brawl ruleset, so <c>AddItem</c> just keeps working
/// past the 12 visible slots (DESIGN.md §3).
/// </summary>
public sealed class LootSystem
{
    private readonly BossRushConfig _cfg;
    private readonly EnhancementSystem _enhancements;
    private readonly Random _rng = new();
    private readonly HashSet<uint> _looted = new(); // container handles already consumed

    // TODO(P2): real loot pool by rarity tier. Real ids look like "upgrade_sprint_booster".
    private static readonly string[] LootPool = { "upgrade_sprint_booster" };

    // TODO(P0/P2): confirm container designer names (gold statue / crate) on the live map.
    private static readonly HashSet<string> LootContainers = new() { "item_crate" };

    public LootSystem(BossRushConfig cfg, EnhancementSystem enhancements)
    {
        _cfg = cfg;
        _enhancements = enhancements;
    }

    public void OnEntityTouch(EntityTouchEvent e)
    {
        // TODO(P0/P2): confirm EntityTouchEvent member names (touched entity + toucher pawn).
        //   var container = e.Entity; var pawn = e.Other?.As<CCitadelPlayerPawn>();
        //   if (!LootContainers.Contains(container.DesignerName) || pawn == null) return;
        //   if (!_looted.Add(container.EntityHandle)) return; // already looted
        //   GrantLoot(pawn);
        //   container.Remove();
    }

    private void GrantLoot(CCitadelPlayerPawn pawn)
    {
        var itemName = LootPool[_rng.Next(LootPool.Length)];
        if (_rng.NextDouble() < _cfg.EnhancedDropChance)
            _enhancements.GrantTemporaryEnhanced(pawn, itemName); // rare, temporary
        else
            pawn.AddItem(itemName); // permanent base item; uncapped under Street Brawl rules
    }
}
