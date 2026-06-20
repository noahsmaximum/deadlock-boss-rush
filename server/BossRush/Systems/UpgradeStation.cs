using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md #5 — the shop is replaced by an Upgrade Station. Buying base items outright is gone (power comes
/// from world loot); the store only offers two things, both priced off the item's tier:
///   • Enhance a held item → <see cref="BossRushConfig.UpgradeCostMultiplier"/>× its tier price (temporary).
///   • Buy a legendary (top-tier) item → flat <see cref="BossRushConfig.LegendaryPrice"/>.
/// Driven by <c>!enhance</c>/<c>!buylegendary</c> commands for now; the storefront→station presentation (tabs,
/// proximity zone) is a client VPK mod (P4). Tier/price come from <see cref="ItemCatalog"/> + config.
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

    /// <summary>Entering a station trigger zone (P4). For now, the station is driven by commands.</summary>
    public void OnEntityTouch(EntityTouchEvent e)
    {
        // TODO(P4): detect a station zone; the client mod surfaces the tabs + prices.
    }

    private int TierPrice(int tier) =>
        tier >= 1 && tier < _cfg.ItemTierPrices.Length ? _cfg.ItemTierPrices[tier] : 0;

    private static bool Holds(CCitadelPlayerPawn pawn, string itemName) =>
        pawn.AbilityComponent.FindAbilityByName(itemName) != null;

    // ── Enhance a held item (2× its tier price) ─────────────────────────────────────

    public void HandleEnhanceCommand(CCitadelPlayerController caller, string itemName)
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null) return;
        TryEnhance(pawn, itemName, out var msg);
        Chat.PrintToChat(caller, msg);
    }

    /// <summary>Charge 2× the item's tier price (souls), then grant its temporary enhanced version. Requires holding
    /// the base item.</summary>
    public bool TryEnhance(CCitadelPlayerPawn pawn, string itemName, out string message)
    {
        int tier = ItemCatalog.TierOf(itemName);
        if (tier <= 0) { message = $"[Boss Rush] unknown item '{itemName}'."; return false; }
        if (!Holds(pawn, itemName)) { message = $"[Boss Rush] you must hold {Pretty(itemName)} to enhance it."; return false; }

        int cost = (int)MathF.Round(TierPrice(tier) * _cfg.UpgradeCostMultiplier);
        if (pawn.GetCurrency(ECurrencyType.EGold) < cost) { message = $"[Boss Rush] need {cost} souls to enhance {Pretty(itemName)}."; return false; }

        // ECheats source so a future BlockNativePurchases (which targets EItemPurchase) won't catch the station.
        pawn.ModifyCurrency(ECurrencyType.EGold, -cost, ECurrencySource.ECheats, spendOnly: true);
        _enhancements.GrantTemporaryEnhanced(pawn, itemName);
        message = _cfg.EnhancementPermanent
            ? $"[Boss Rush] Enhanced {Pretty(itemName)} (T{tier}) for {cost}."
            : $"[Boss Rush] Enhanced {Pretty(itemName)} (T{tier}) for {cost} — lasts {_cfg.EnhancementDurationSeconds:F0}s.";
        return true;
    }

    // ── Buy a legendary (top-tier) item (flat price) ────────────────────────────────

    public void HandleBuyLegendaryCommand(CCitadelPlayerController caller, string itemName)
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null) return;
        TryBuyLegendary(pawn, itemName, out var msg);
        Chat.PrintToChat(caller, msg);
    }

    /// <summary>Buy a legendary (tier <see cref="BossRushConfig.LegendaryTier"/>) item for the flat legendary price.</summary>
    public bool TryBuyLegendary(CCitadelPlayerPawn pawn, string itemName, out string message)
    {
        int tier = ItemCatalog.TierOf(itemName);
        if (tier != _cfg.LegendaryTier) { message = $"[Boss Rush] {Pretty(itemName)} isn't a legendary (T{_cfg.LegendaryTier})."; return false; }
        if (Holds(pawn, itemName)) { message = $"[Boss Rush] you already hold {Pretty(itemName)}."; return false; }
        if (pawn.GetCurrency(ECurrencyType.EGold) < _cfg.LegendaryPrice) { message = $"[Boss Rush] need {_cfg.LegendaryPrice} souls for {Pretty(itemName)}."; return false; }

        pawn.ModifyCurrency(ECurrencyType.EGold, -_cfg.LegendaryPrice, ECurrencySource.ECheats, spendOnly: true);
        pawn.AddItem(itemName);
        message = $"[Boss Rush] Purchased legendary {Pretty(itemName)} for {_cfg.LegendaryPrice}.";
        return true;
    }

    private static string Pretty(string itemName)
    {
        var s = itemName.StartsWith("upgrade_", StringComparison.OrdinalIgnoreCase) ? itemName["upgrade_".Length..] : itemName;
        return s.Replace('_', ' ');
    }
}
