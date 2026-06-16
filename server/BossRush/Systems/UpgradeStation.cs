using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md #5 — the shop becomes an Upgrade Station. There is no shop-catalog API, so this is a
/// *station interaction*: enhance an item you already hold to its (temporary) enhanced version for
/// <see cref="BossRushConfig.UpgradeCostMultiplier"/>× its normal price. Driven by an
/// <c>!upgrade &lt;item&gt;</c> command for now; a proximity zone + HUD prompt comes in P2.
/// </summary>
public sealed class UpgradeStation
{
    private readonly BossRushConfig _cfg;
    private readonly EnhancementSystem _enhancements;

    public UpgradeStation(BossRushConfig cfg, EnhancementSystem enhancements)
    {
        _cfg = cfg;
        _enhancements = enhancements;
    }

    /// <summary>Entering a station trigger zone (P2). For now, upgrades go through the command.</summary>
    public void OnEntityTouch(EntityTouchEvent e)
    {
        // TODO(P2): detect a station zone; prompt the toucher with their upgradeable items.
    }

    public void HandleUpgradeCommand(CCitadelPlayerController caller, string itemName)
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null) return;

        if (TryUpgrade(pawn, itemName))
            Chat.PrintToChat(caller, $"[Boss Rush] Enhanced {itemName} for {_cfg.EnhancementDurationSeconds:F0}s.");
        else
            Chat.PrintToChat(caller, $"[Boss Rush] Can't enhance {itemName} (not held or not enough souls).");
    }

    /// <summary>Charge 2× the item's price (souls = EGold), then grant a temporary enhanced version.</summary>
    public bool TryUpgrade(CCitadelPlayerPawn pawn, string itemName)
    {
        var cost = (int)MathF.Round(ShopPriceOf(itemName) * _cfg.UpgradeCostMultiplier);

        if (pawn.GetCurrency(ECurrencyType.EGold) < cost) return false;
        // TODO(P0/P2): confirm holding `itemName` before charging (avoid enhancing nothing).
        pawn.ModifyCurrency(ECurrencyType.EGold, -cost, ECurrencySource.EItemPurchase, spendOnly: true);

        _enhancements.GrantTemporaryEnhanced(pawn, itemName);
        return true;
    }

    // TODO(P2): source the real per-item price from item VData. Hard-coded fallback for now.
    private static int ShopPriceOf(string itemName) => 500;
}
