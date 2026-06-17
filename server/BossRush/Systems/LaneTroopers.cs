using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// Spawns Hidden-King lane troopers via the game's zipline spawner. Each
/// <c>citadel_spawn_trooper_zipline &lt;team&gt; &lt;lane&gt;</c> drops one trooper onto a team's lane
/// transit (it then rides in and marches), so enemies pour out of the Hidden King's lanes rather
/// than appearing near a player. Cheat-gated, so we ensure <c>sv_cheats</c> and run server-side.
/// dl_midtown Hidden King lanes (confirmed in-game): 1 = left, 4 = middle, 6 = right.
/// </summary>
internal static class LaneTroopers
{
    /// <summary>Spawn <paramref name="perLane"/> troopers on every parsed lane (even reinforcement).</summary>
    public static void SpawnPerLane(int team, int perLane, string lanesCsv)
    {
        var lanes = ParseLanes(lanesCsv);
        if (lanes.Length == 0 || perLane <= 0) return;

        ConVar.Find("sv_cheats")?.SetInt(1);
        foreach (var lane in lanes)
            for (int i = 0; i < perLane; i++)
                Server.ExecuteCommand($"citadel_spawn_trooper_zipline {team} {lane}");
    }

    /// <summary>Spawn <paramref name="total"/> troopers spread round-robin across the parsed lanes (rage burst).</summary>
    public static void SpawnAcross(int team, int total, string lanesCsv)
    {
        var lanes = ParseLanes(lanesCsv);
        if (lanes.Length == 0 || total <= 0) return;

        ConVar.Find("sv_cheats")?.SetInt(1);
        for (int i = 0; i < total; i++)
            Server.ExecuteCommand($"citadel_spawn_trooper_zipline {team} {lanes[i % lanes.Length]}");
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
