using System.Numerics;
using System.Text.Json;
using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// Developer / diagnostic commands — the toolkit for the P0 live experiments in
/// docs/VERIFIED_API.md §9: discover the real NPC designer names to spawn, record world
/// coordinates, and confirm the active ruleset. Pure read-only verified APIs (no runtime
/// guesses), so these compile and behave deterministically the moment a server is up.
///
/// Invoke from the server console (e.g. <c>br_dumpents</c>) or in chat (<c>!br_pos</c>):
///   br_dumpents [path] — dump every server entity (designer name, team, pos, hp) to JSON,
///                        plus a count-by-designer-name summary → reveals real classnames
///                        (npc_trooper, npc_boss_tier1/2/3, crate/rejuv, …) to spawn.
///   br_nearby [radius] — list entities near you, closest first (walk up to a trooper /
///                        guardian / crate to learn its designer name).
///   br_pos             — print your position + camera angles as JSON (record spawn / Upgrade
///                        Station / crystal-buff coordinates for config).
///   br_gamestate       — print GameRules: mode (confirms Street Brawl), state, clock,
///                        midboss & rejuvenator counts.
/// </summary>
public sealed partial class BossRushPlugin
{
    [Command("br_dumpents", Description = "Dump all server entities to JSON + summary", ServerOnly = true)]
    public void CmdDumpEntities(string outputPath = "")
    {
        var entities = Entities.All
            .Select(e => new
            {
                designer = e.DesignerName,
                team = e.TeamNum,
                alive = e.IsAlive,
                hp = e.Health,
                pos = new[] { e.Position.X, e.Position.Y, e.Position.Z },
            })
            .ToList();

        var byDesigner = entities
            .GroupBy(e => e.designer)
            .Select(g => new { designer = g.Key, count = g.Count() })
            .OrderByDescending(g => g.count)
            .ToList();

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "deadlock_dumps");
            Directory.CreateDirectory(dir);
            outputPath = Path.Combine(dir, $"entdump_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.json");
        }

        File.WriteAllText(outputPath, JsonSerializer.Serialize(
            new { map = Server.MapName, byDesigner, entities },
            new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"[Boss Rush] Dumped {entities.Count} entities ({byDesigner.Count} distinct designer names) → {outputPath}");
        foreach (var g in byDesigner.Take(40))
            Console.WriteLine($"  {g.count,5}  {g.designer}");
    }

    [Command("br_nearby", Description = "List entities near you, closest first")]
    public void CmdNearby(CCitadelPlayerController caller, string radius = "800")
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null) return;
        if (!float.TryParse(radius, out var r)) r = 800f;
        var origin = pawn.Position;

        var near = Entities.All
            .Where(e => !string.IsNullOrEmpty(e.DesignerName) && e.EntityIndex != pawn.EntityIndex)
            .Select(e => (e, dist: Vector3.Distance(e.Position, origin)))
            .Where(x => x.dist <= r)
            .OrderBy(x => x.dist)
            .Take(15)
            .ToList();

        Chat.PrintToChat(caller, $"[Boss Rush] {near.Count} entities within {r:F0}u:");
        foreach (var (e, dist) in near)
            Chat.PrintToChat(caller, $"  {dist,5:F0}u  {e.DesignerName}  (team {e.TeamNum}, hp {e.Health})");
    }

    [Command("br_pos", Description = "Print your position and camera angles as JSON")]
    public void CmdPos(CCitadelPlayerController caller)
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null) return;
        var p = pawn.Position;
        var a = pawn.CameraAngles;
        var line = $"{{ \"pos\": [{p.X:F1}, {p.Y:F1}, {p.Z:F1}], \"ang\": [{a.X:F1}, {a.Y:F1}, {a.Z:F1}] }}";
        Console.WriteLine($"[Boss Rush] {line}");
        Chat.PrintToChat(caller, line);
    }

    [Command("br_gamestate", Description = "Print GameRules state (mode/state/clock/counts)")]
    public void CmdGameState(CCitadelPlayerController caller)
    {
        var s =
            $"mode={GameRules.GameMode} state={GameRules.GameState} clock={GameRules.GameClock:F0}s " +
            $"midboss={GameRules.MidbossKillCount} amber={GameRules.AmberRejuvCount} sapphire={GameRules.SapphireRejuvCount}";
        Console.WriteLine($"[Boss Rush] {s}");
        Chat.PrintToChat(caller, s);
    }
}
