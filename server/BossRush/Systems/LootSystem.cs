using DeadworksManaged.Api; // ⚠️ provisional — verify in P0

namespace BossRush;

/// <summary>
/// DESIGN.md #4 &amp; #6 — power comes from the world, not the shop. Breaking/touching world
/// containers (gold statues, crates — Deadlock's analog to "golden buddhas") grants an item;
/// a tiny fraction roll an *enhanced* item instead. Souls and permanent power-ups stay native.
/// </summary>
public sealed class LootSystem
{
    private readonly BossRushConfig _cfg;
    private readonly RelicSystem _relics;
    private readonly Random _rng = new();

    // TODO(P2): the real loot pool — item IDs by rarity tier. Real IDs look like
    // "upgrade_sprint_booster" (seen in the Deadworks ItemRotation example).
    private static readonly string[] LootPool =
    {
        "upgrade_sprint_booster",
        // … fill from Deadlock's item list during P2
    };

    public LootSystem(BossRushConfig cfg, RelicSystem relics)
    {
        _cfg = cfg;
        _relics = relics;
    }

    /// <summary>Called from the plugin's entity-touch hook. Filters for loot containers.</summary>
    public void OnEntityTouch(EntityTouchEvent e)
    {
        // TODO(P2): confirm container classnames (gold statue / item_crate) and that `e`
        // exposes the toucher pawn + the touched entity. Only react to a hero touching a
        // not-yet-looted container, then consume it.
        //
        //   if (!IsLootContainer(e.Touched)) return;
        //   if (e.Toucher is not CCitadelPlayerPawn pawn) return;
        //   GrantLoot(pawn);
        //   e.Touched.Remove();
    }

    private void GrantLoot(CCitadelPlayerPawn pawn)
    {
        var itemName = LootPool[_rng.Next(LootPool.Length)];
        var enhanced = _rng.NextDouble() < _cfg.EnhancedDropChance;

        // Past the real-slot cap, route the pick to the Relic system (modifier-based) instead
        // of AddItem, which would return null on a full inventory (DESIGN.md §3).
        if (_relics.RealItemCount(pawn) >= _cfg.RealItemSlotCap)
            _relics.GrantRelic(pawn, itemName, enhanced);
        else
            pawn.AddItem(itemName, enhanced); // native "enhanced" flag (DESIGN.md confirms)
    }
}
