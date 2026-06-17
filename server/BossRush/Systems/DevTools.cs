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
    public void CmdGameState(CCitadelPlayerController? caller = null)
    {
        var s =
            $"mode={GameRules.GameMode} state={GameRules.GameState} clock={GameRules.GameClock:F0}s " +
            $"midboss={GameRules.MidbossKillCount} amber={GameRules.AmberRejuvCount} sapphire={GameRules.SapphireRejuvCount}";
        Console.WriteLine($"[Boss Rush] {s}");
        if (caller != null)
            Chat.PrintToChat(caller, s);
    }

    // Designer names that crash native Spawn() when created standalone: they need the game's
    // lane/spawner context (CCitadelTrooper derefs a null lane). Refuse rather than AV the server.
    private static readonly HashSet<string> UnsafeToSpawn = new(StringComparer.OrdinalIgnoreCase)
    {
        "npc_trooper", "npc_super_trooper",
    };

    [Command("br_spawn", Description = "Spawn one entity by designer name in front of you (dev)")]
    public void CmdSpawn(CCitadelPlayerController caller, string designerName, string team = "3", string hp = "")
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null) return;

        if (UnsafeToSpawn.Contains(designerName))
        {
            Chat.PrintToChat(caller,
                $"[Boss Rush] '{designerName}' needs the lane spawner — a direct spawn crashes the server; skipped.");
            return;
        }

        if (!int.TryParse(team, out var teamNum)) teamNum = BossRushPlugin.EnemyTeam;

        // Place it ~200u in front of where you're facing, lifted off the ground a touch.
        var yaw = pawn.CameraAngles.Y * (MathF.PI / 180f);
        var origin = pawn.Position
            + new Vector3(MathF.Cos(yaw), MathF.Sin(yaw), 0f) * 200f
            + new Vector3(0f, 0f, 16f);

        var ent = CBaseEntity.CreateByDesignerName(designerName);
        if (ent == null)
        {
            var miss = $"[Boss Rush] CreateByDesignerName('{designerName}') returned null";
            Console.WriteLine(miss);
            Chat.PrintToChat(caller, miss);
            return;
        }

        ent.TeamNum = teamNum;
        ent.Teleport(position: origin);
        ent.Spawn();

        // Optional hp override — neutrals spawn at 1 hp, so let dev tests make them a real threat.
        if (int.TryParse(hp, out var hpVal) && hpVal > 0)
        {
            ent.MaxHealth = hpVal;
            ent.Health = hpVal;
        }

        var msg = $"[Boss Rush] spawned '{designerName}' -> class {ent.Classname}, team {teamNum}, " +
                  $"valid={ent.IsValid}, hp={ent.Health}/{ent.MaxHealth} at {origin.X:F0},{origin.Y:F0},{origin.Z:F0}";
        Console.WriteLine(msg);
        Chat.PrintToChat(caller, msg);
    }

    [Command("br_cmds", Description = "List convars/concommands matching a filter, to console (dev)")]
    public void CmdListCommands(string filter = "")
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            Console.WriteLine("[Boss Rush] usage: br_cmds <substring>  (e.g. trooper, spawn, lane, barrack)");
            return;
        }

        bool Match(string? s) => s != null && s.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var cmds = Server.EnumerateConCommands()
            .Where(c => Match(c.Name) || Match(c.Description))
            .OrderBy(c => c.Name).ToList();
        var cvars = Server.EnumerateConVars()
            .Where(c => Match(c.Name) || Match(c.Description))
            .OrderBy(c => c.Name).ToList();

        Console.WriteLine($"[Boss Rush] '{filter}': {cmds.Count} concommands, {cvars.Count} convars");
        foreach (var c in cmds.Take(80))
            Console.WriteLine($"  [cmd] {c.Name}  -  {c.Description}");
        foreach (var c in cvars.Take(80))
            Console.WriteLine($"  [var] {c.Name} = {c.Value}  -  {c.Description}");
    }

    [Command("br_run", Description = "Run a server console command w/ sv_cheats (dev). e.g. br_run citadel_spawn_trooper_grid 3")]
    public void CmdRun(CCitadelPlayerController? caller = null, string command = "", string a1 = "", string a2 = "", string a3 = "")
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            Console.WriteLine("[Boss Rush] usage: br_run <command> [arg] [arg] [arg]");
            return;
        }

        // Cheat-gated commands (citadel_spawn_trooper, ...) are rejected when the *client* runs
        // them, but the server console can. Force sv_cheats server-side and run via ExecuteCommand.
        ConVar.Find("sv_cheats")?.SetInt(1);
        var full = string.Join(" ", new[] { command, a1, a2, a3 }.Where(s => !string.IsNullOrEmpty(s)));
        Server.ExecuteCommand(full);

        var msg = $"[Boss Rush] server-ran: {full}";
        Console.WriteLine(msg);
        if (caller != null) Chat.PrintToChat(caller, msg);
    }

    [Command("br_ragewave", Description = "Trigger a rage wave now (dev)")]
    public void CmdRageWave(CCitadelPlayerController? caller = null)
    {
        _rageWaves.TriggerNow();
        var msg = "[Boss Rush] rage wave triggered (dev)";
        Console.WriteLine(msg);
        if (caller != null) Chat.PrintToChat(caller, msg);
    }

    [Command("br_heal", Description = "Test healing yourself by N — reports Heal() vs direct set (dev)")]
    public void CmdHeal(CCitadelPlayerController caller, string amount = "200")
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null) return;
        if (!float.TryParse(amount, out var amt)) amt = 200f;

        int before = pawn.Health;
        int healed = pawn.Heal(amt);
        int afterHeal = pawn.Health;

        string viaSet = "";
        if (afterHeal == before) // Heal() did nothing — try a direct schema write
        {
            pawn.Health = Math.Min(before + (int)amt, pawn.MaxHealth);
            viaSet = $"; directSet -> {pawn.Health}";
        }

        var msg = $"[Boss Rush] hp {before}/{pawn.MaxHealth}; Heal({amt}) returned {healed} -> {afterHeal}{viaSet}";
        Console.WriteLine(msg);
        Chat.PrintToChat(caller, msg);
    }
}
