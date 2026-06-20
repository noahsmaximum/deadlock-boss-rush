namespace BossRush;

/// <summary>
/// Item → tier lookup, loaded from <c>all_items_tiers.txt</c> ("upgrade_name,tier" per line — extracted from
/// <c>m_iItemTier</c> in the live abilities.vdata). Tier 5 = legendary. Pricing lives in config
/// (<see cref="BossRushConfig.ItemTierPrices"/>, mirroring <c>m_nItemPricePerTier</c>).
/// </summary>
public static class ItemCatalog
{
    private static readonly Dictionary<string, int> _tier = Load();

    public static int Count => _tier.Count;

    private static Dictionary<string, int> Load()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "deadlock_dumps", "all_items_tiers.txt");
        if (File.Exists(path))
            foreach (var raw in File.ReadAllLines(path))
            {
                var parts = raw.Split(',');
                if (parts.Length == 2 && int.TryParse(parts[1].Trim(), out var t) && t >= 1 && t <= 5)
                    map[parts[0].Trim()] = t;
            }
        return map;
    }

    /// <summary>The item's tier (1..5), or 0 if unknown.</summary>
    public static int TierOf(string itemName) => _tier.TryGetValue(itemName, out var t) ? t : 0;
}
