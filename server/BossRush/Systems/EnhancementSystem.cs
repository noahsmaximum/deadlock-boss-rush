using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md #5, #6, #8 — *enhanced* items are temporary: they last
/// <see cref="BossRushConfig.EnhancementDurationSeconds"/> (5 min) or until the holder dies,
/// then revert to the base item. Used by both the Upgrade Station (paid) and rare world drops.
///
/// Items themselves are uncapped under the Street Brawl ruleset (DESIGN.md §3), so we grant the
/// native enhanced variant via <c>AddItem(name, enhanced: true)</c> and schedule a revert.
/// Per-player timers are stored in <see cref="EntityData{T}"/> (keyed by pawn).
/// </summary>
public sealed class EnhancementSystem
{
    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;
    private readonly EntityData<Dictionary<string, IHandle>> _active = new();

    public EnhancementSystem(BossRushConfig cfg, ITimer timer)
    {
        _cfg = cfg;
        _timer = timer;
    }

    /// <summary>Give (or refresh) a temporary enhanced version of <paramref name="itemName"/>.</summary>
    public void GrantTemporaryEnhanced(CCitadelPlayerPawn pawn, string itemName)
    {
        pawn.RemoveItem(itemName);            // drop the base variant if held (no-op otherwise)
        pawn.AddItem(itemName, enhanced: true);

        if (!_active.TryGet(pawn, out var byItem))
            _active[pawn] = byItem = new Dictionary<string, IHandle>();
        if (byItem.TryGetValue(itemName, out var existing))
            existing.Cancel(); // refresh the timer if re-enhanced

        byItem[itemName] = _timer.Once(((int)_cfg.EnhancementDurationSeconds).Seconds(),
            () => Revert(pawn, itemName));
    }

    private void Revert(CCitadelPlayerPawn pawn, string itemName)
    {
        pawn.RemoveItem(itemName);
        pawn.AddItem(itemName, enhanced: false); // back to base — keep the item, lose the enhancement
        if (_active.TryGet(pawn, out var byItem))
            byItem.Remove(itemName);
    }

    /// <summary>On death, strip all active enhancements back to base (DESIGN.md #8).</summary>
    public void OnPlayerDeath(CCitadelPlayerPawn pawn)
    {
        if (!_active.TryGet(pawn, out var byItem)) return;
        foreach (var (itemName, handle) in byItem)
        {
            handle.Cancel();
            // TODO(P2): confirm items persist through death/respawn so revert targets the right pawn.
            pawn.RemoveItem(itemName);
            pawn.AddItem(itemName, enhanced: false);
        }
        byItem.Clear();
    }

    public void Clear() => _active.Clear();
}
