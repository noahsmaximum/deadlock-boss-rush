using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// Spawns Hidden-King lane troopers via the game's zipline spawner. Each
/// <c>citadel_spawn_trooper_zipline &lt;team&gt; &lt;lane&gt;</c> drops one trooper onto a team's lane
/// transit (it then rides in and marches the lane), so enemies pour out of the Hidden King's lanes
/// rather than appearing near a player. The command is cheat-gated, so we ensure <c>sv_cheats</c>
/// and run it server-side. Lane indices are map-specific — configure via
/// <see cref="BossRushConfig.HiddenKingLanesCsv"/>.
/// </summary>
internal static class LaneTroopers
{
    /// <summary>Spawn <paramref name="count"/> troopers for <paramref name="team"/>, round-robin across the parsed lanes.</summary>
    public static void Spawn(int team, int count, string lanesCsv)
    {
        var lanes = ParseLanes(lanesCsv);
        if (lanes.Length == 0 || count <= 0) return;

        ConVar.Find("sv_cheats")?.SetInt(1);
        for (int i = 0; i < count; i++)
        {
            int lane = lanes[i % lanes.Length];
            Server.ExecuteCommand($"citadel_spawn_trooper_zipline {team} {lane}");
        }
    }

    private static int[] ParseLanes(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<int>();
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var lanes = new List<int>(parts.Length);
        foreach (var p in parts)
            if (int.TryParse(p, out var n)) lanes.Add(n);
        return lanes.ToArray();
    }
}
