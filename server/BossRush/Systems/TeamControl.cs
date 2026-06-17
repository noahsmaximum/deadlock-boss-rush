using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// PvE co-op team enforcement. Every human plays on the Archmother's team
/// (<see cref="BossRushPlugin.HeroTeam"/>); the Hidden King's team (<see cref="BossRushPlugin.EnemyTeam"/>)
/// is AI-only and its lanes are the threat. The team-select UI is client-side, so we make the
/// Hidden King "unselectable" the only way we can server-side: anyone who isn't on the hero team
/// gets moved back. A periodic sweep covers join, respawn, and any switch attempt uniformly.
/// </summary>
public static class TeamControl
{
    /// <summary>Move one player onto the hero team if they aren't already there.</summary>
    public static void Force(CCitadelPlayerController? controller)
    {
        if (controller != null && controller.TeamNum != BossRushPlugin.HeroTeam)
            controller.ChangeTeam(BossRushPlugin.HeroTeam);
    }

    /// <summary>Move every connected player onto the hero team.</summary>
    public static void ForceAll()
    {
        foreach (var c in Players.GetAll())
            Force(c);
    }
}
