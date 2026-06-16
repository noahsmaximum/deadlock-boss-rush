using DeadworksManaged.Api; // ⚠️ provisional — verify in P0

namespace BossRush;

/// <summary>
/// DESIGN.md #5 — the shop becomes an Upgrade Station. There is no shop-catalog API, so this is
/// a *station interaction*: when a hero is in a station zone, they can upgrade an item they
/// already hold to its enhanced version for <see cref="BossRushConfig.UpgradeCostMultiplier"/>×
/// its normal price. (Reskinning the shop's visuals to look like a station is optional client
/// Panorama work — see client/README.md.)
/// </summary>
public sealed class UpgradeStation
{
    private readonly BossRushConfig _cfg;

    public UpgradeStation(BossRushConfig cfg) => _cfg = cfg;

    /// <summary>Called from the plugin's entity-touch hook for station trigger zones.</summary>
    public void OnEntityTouch(EntityTouchEvent e)
    {
        // TODO(P2): detect entering a station zone; prompt the player (HUD announcement or a
        // chat/console command flow) with which held item to enhance.
    }

    /// <summary>
    /// Performs the upgrade: charge 2× the item's price, then swap the base item for its
    /// enhanced version. Returns false if the player can't afford it / doesn't hold it.
    /// </summary>
    public bool TryUpgrade(CCitadelPlayerPawn pawn, string itemName)
    {
        var price = ShopPriceOf(itemName);
        var cost = (int)MathF.Round(price * _cfg.UpgradeCostMultiplier);

        // TODO(P2): confirm ECurrencyType / ModifyCurrency signature and that we can read the
        // player's balance to gate the purchase.
        //   if (pawn.GetCurrency(ECurrencyType.Souls) < cost) return false;
        //   pawn.ModifyCurrency(ECurrencyType.Souls, -cost, ECurrencySource.Purchase);

        // Swap base → enhanced (the native "enhanced" flag does the heavy lifting).
        pawn.RemoveItem(itemName);
        pawn.AddItem(itemName, enhanced: true);

        _ = cost;
        return true;
    }

    // TODO(P2): source the real per-item shop price (likely from item VData). Hard-coded
    // fallback until then.
    private static int ShopPriceOf(string itemName) => 500;
}
