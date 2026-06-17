using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// Spawns hostile wave troopers through the game's own console spawner — the only path that
/// survives. Raw <c>CreateByDesignerName("npc_trooper") + Spawn()</c> dereferences a null lane
/// and AVs the server (docs/VERIFIED_API.md §11–12); <c>citadel_spawn_trooper(_grid)</c> lets the
/// game do the lane setup. The troopers spawn near a player and are hostile to the heroes.
/// The command is cheat-gated, so we ensure <c>sv_cheats</c> and run it server-side.
/// </summary>
internal static class Troopers
{
    /// <summary>Spawns an NxN grid of hostile troopers near a player (N clamped 1–8).</summary>
    public static void SpawnGrid(int n)
    {
        ConVar.Find("sv_cheats")?.SetInt(1);
        Server.ExecuteCommand($"citadel_spawn_trooper_grid {Math.Clamp(n, 1, 8)}");
    }

    /// <summary>Spawns a single hostile trooper near a player.</summary>
    public static void SpawnOne()
    {
        ConVar.Find("sv_cheats")?.SetInt(1);
        Server.ExecuteCommand("citadel_spawn_trooper");
    }
}
