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
        // Objective/lane NPCs crash on a direct CreateByDesignerName+Spawn — they need the game's own spawn path.
        "npc_trooper", "npc_super_trooper",
        "npc_trooper_boss", "npc_barrack_boss", "npc_boss_tier1", "npc_boss_tier2", "npc_boss_tier3",
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

    [Command("br_additem", Description = "Give yourself an item by internal name — test which moves the HUD regen stat (dev)")]
    public void CmdAddItem(CCitadelPlayerController caller, string itemName, string enhanced = "")
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null) return;
        bool enh = enhanced is "1" or "true" or "enhanced";
        var item = pawn.AddItem(itemName, enh);
        var msg = $"[Boss Rush] AddItem('{itemName}', enhanced={enh}) => {(item != null ? item.ToString() : "NULL (bad name?)")}";
        Console.WriteLine(msg);
        Chat.PrintToChat(caller, msg);
    }

    [Command("br_bossinfo", Description = "Report the Hidden King + its native ability entities (dev)")]
    public void CmdBossInfo(CCitadelPlayerController? caller = null)
    {
        var status = _patron.DebugStatus();
        Console.WriteLine($"[Boss Rush] {status}");
        if (caller != null) Chat.PrintToChat(caller, status);

        // The native Patron brings child ability ents (citadel_ability_tier3boss_*). List anything
        // tier3-boss-related so we learn the real designer names to drive via AcceptInput (decision #6).
        var ents = Entities.All
            .Where(e => !string.IsNullOrEmpty(e.DesignerName) &&
                        (e.DesignerName.Contains("tier3boss", StringComparison.OrdinalIgnoreCase) ||
                         e.DesignerName.Contains("boss_tier3", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Console.WriteLine($"[Boss Rush] {ents.Count} tier3-boss-related ents:");
        foreach (var e in ents)
            Console.WriteLine($"  {e.DesignerName}  (class {e.Classname}, team {e.TeamNum}, hp {e.Health}, handle {e.EntityHandle})");
    }

    [Command("br_bossult", Description = "Fire one Hidden King ult now (dev). index: 0=laser 1=storm 2=barrage 3=bombs 4=sleep")]
    public void CmdBossUlt(CCitadelPlayerController? caller = null, string index = "0")
    {
        if (!int.TryParse(index, out var i)) i = 0;
        bool ok = _patron.DebugFireUlt(i);
        var msg = ok
            ? $"[Boss Rush] fired ult #{i}. {_patron.DebugStatus()}"
            : "[Boss Rush] no Patron found — spawn npc_boss_tier3 first.";
        Console.WriteLine(msg);
        if (caller != null) Chat.PrintToChat(caller, msg);
    }

    [Command("br_bossinput", Description = "AcceptInput(<input>) on every ent whose designer contains <substr> — probe native ability triggers (dev)")]
    public void CmdBossInput(CCitadelPlayerController? caller = null, string substr = "tier3boss", string input = "")
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            Console.WriteLine("[Boss Rush] usage: br_bossinput <designer-substr> <input>  (e.g. br_bossinput tier3boss_laser FireAbility)");
            return;
        }

        var targets = Entities.All
            .Where(e => !string.IsNullOrEmpty(e.DesignerName) &&
                        e.DesignerName.Contains(substr, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var e in targets)
        {
            e.AcceptInput(input);
            Console.WriteLine($"[Boss Rush] AcceptInput('{input}') -> {e.DesignerName}");
        }

        var msg = $"[Boss Rush] sent '{input}' to {targets.Count} ent(s) matching '{substr}'";
        Console.WriteLine(msg);
        if (caller != null) Chat.PrintToChat(caller, msg);
    }

    [Command("br_bossfire", Description = "Fire a REAL boss ability via its test cvar (dev). keys: laser, barrage, bomb, smash, shrine, phase2 [resetSec=1.5]")]
    public void CmdBossFire(CCitadelPlayerController? caller = null, string ability = "laser", string resetSec = "1.5")
    {
        if (!float.TryParse(resetSec, out var rs)) rs = 1.5f;
        var msg = $"[Boss Rush] {_patron.DebugFireNative(ability, rs)}";
        Console.WriteLine(msg);
        if (caller != null) Chat.PrintToChat(caller, msg);
    }

    [Command("br_bosspromote", Description = "Target the existing team-2 Patron as the Hidden King (resize hp if given) — no duplicate spawn (dev)")]
    public void CmdBossPromote(CCitadelPlayerController? caller = null, string hp = "0")
    {
        if (!int.TryParse(hp, out var hpVal)) hpVal = 0;
        bool ok = _patron.DebugPromote(hpVal);
        var msg = ok
            ? $"[Boss Rush] promoted enemy Patron. {_patron.DebugStatus()}"
            : "[Boss Rush] no team-2 Patron found to promote.";
        Console.WriteLine(msg);
        if (caller != null) Chat.PrintToChat(caller, msg);
    }

    [Command("br_mod", Description = "Apply a modifier to yourself by name for testing (dev). e.g. br_mod modifier_familiar_asleep 4")]
    public void CmdMod(CCitadelPlayerController caller, string modifier, string duration = "4")
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null) return;
        if (!float.TryParse(duration, out var dur)) dur = 4f;

        using var kv = new KeyValues3();
        kv.SetFloat("duration", dur);
        var mod = pawn.AddModifier(modifier, kv, caster: pawn);

        var msg = $"[Boss Rush] AddModifier('{modifier}', {dur}s) => {(mod != null ? "applied" : "NULL (bad name?)")}";
        Console.WriteLine(msg);
        Chat.PrintToChat(caller, msg);
    }

    [Command("br_sound", Description = "EmitSound a soundevent by name to test which fire (dev). e.g. br_sound MidBoss.Arrival [all]")]
    public void CmdSound(CCitadelPlayerController caller, string soundName, string scope = "self")
    {
        var pawns = scope.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? Players.GetAllPawns().Where(p => p.Health > 0).ToList()
            : new List<CCitadelPlayerPawn>();
        if (pawns.Count == 0 && caller.GetHeroPawn()?.As<CCitadelPlayerPawn>() is { } self)
            pawns.Add(self);

        foreach (var p in pawns) p.EmitSound(soundName);
        var msg = $"[Boss Rush] EmitSound('{soundName}') on {pawns.Count} pawn(s) — listen.";
        Console.WriteLine(msg);
        Chat.PrintToChat(caller, msg);
    }

    [Command("br_reloadcfg", Description = "Reload BossRushPlugin.jsonc and report key values (dev)")]
    public void CmdReloadCfg(CCitadelPlayerController? caller = null)
    {
        bool ok = this.ReloadConfig();
        var msg = $"[Boss Rush] config reload={ok} RageWaveStartSound='{Config.RageWaveStartSound}' native={Config.BossUseNativeAbilities}";
        Console.WriteLine(msg);
        if (caller != null) Chat.PrintToChat(caller, msg);
    }

    // Item names are data, not code — regenerate via Source2Viewer-CLI on abilities.vdata_c (upgrade_* top-level keys).
    private static string ItemListPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "deadlock_dumps", "all_items.txt");

    private static List<string> LoadItemNames()
    {
        var list = new List<string>();
        if (File.Exists(ItemListPath))
            foreach (var raw in File.ReadAllLines(ItemListPath))
            {
                var n = raw.Trim();
                if (n.Length > 0 && !n.StartsWith("//")) list.Add(n);
            }
        return list;
    }

    /// <summary>Set level AND grant the souls + ability points + unlocks needed to fully kit out a hero.
    /// pawn.Level alone only yields the few auto-granted points (the "6 points / one ability" bug) — the
    /// ability-unlock/upgrade economy is the separate EAbilityPoints / EAbilityUnlocks currencies.</summary>
    private static void GrantFullPower(CCitadelPlayerPawn pawn, int level)
    {
        if (level > 0) pawn.Level = level;
        pawn.ModifyCurrency(ECurrencyType.EGold, 1_000_000, ECurrencySource.ECheats, forceGain: true);
        pawn.ModifyCurrency(ECurrencyType.EAbilityPoints, 50, ECurrencySource.ECheats, forceGain: true);
        pawn.ModifyCurrency(ECurrencyType.EAbilityUnlocks, 16, ECurrencySource.ECheats, forceGain: true);
    }

    [Command("br_allitems", Description = "Dev: give yourself every item + level + souls + ability points. e.g. br_allitems [level=30]")]
    public void CmdAllItems(CCitadelPlayerController caller, string level = "30")
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null) return;

        int lvl = int.TryParse(level, out var l) && l > 0 ? l : 30;
        GrantFullPower(pawn, lvl);

        var all = LoadItemNames();
        int added = 0, failed = 0;
        foreach (var n in all)
            if (pawn.AddItem(n) != null) added++; else failed++;

        var msg = all.Count > 0
            ? $"[Boss Rush] level={pawn.Level}, +1M souls + AP, items: {added} added / {failed} failed (of {added + failed})."
            : $"[Boss Rush] level={pawn.Level}, +1M souls + AP. Item list missing: {ItemListPath}";
        Console.WriteLine(msg);
        Chat.PrintToChat(caller, msg);
    }

    [Command("br_randomitems", Description = "Dev: random N items (default 18) + level + souls + ability points. e.g. br_randomitems [count=18] [level=30]")]
    public void CmdRandomItems(CCitadelPlayerController caller, string count = "18", string level = "30")
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null) return;

        int n = int.TryParse(count, out var c) && c > 0 ? c : 18;
        int lvl = int.TryParse(level, out var l) && l > 0 ? l : 30;
        GrantFullPower(pawn, lvl);

        var all = LoadItemNames();
        if (all.Count == 0) { Chat.PrintToChat(caller, $"[Boss Rush] item list missing: {ItemListPath}"); return; }

        var rng = new Random();
        var pick = all.OrderBy(_ => rng.Next()).Take(Math.Min(n, all.Count)).ToList();
        int added = 0, failed = 0;
        foreach (var name in pick)
            if (pawn.AddItem(name) != null) added++; else failed++;

        var msg = $"[Boss Rush] level={pawn.Level}, +1M souls + AP, random items: {added} added / {failed} failed (requested {n} of {all.Count}).";
        Console.WriteLine(msg);
        Chat.PrintToChat(caller, msg);
    }

    [Command("br_level", Description = "Dev: set level + grant souls/ability points/unlocks (no items). e.g. br_level [level=30]")]
    public void CmdLevel(CCitadelPlayerController caller, string level = "30")
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null) return;
        int lvl = int.TryParse(level, out var l) && l > 0 ? l : 30;
        GrantFullPower(pawn, lvl);
        var msg = $"[Boss Rush] level={pawn.Level}, +1M souls, +50 AP, +16 unlocks. Unlock/upgrade abilities now.";
        Console.WriteLine(msg);
        Chat.PrintToChat(caller, msg);
    }

    [Command("br_doubleguardians", Description = "Dev: spawn a twin next to each enemy Guardian (fight 2 instead of 1).")]
    public void CmdDoubleGuardians(CCitadelPlayerController caller)
    {
        var msg = _spawns.DoubleGuardiansNow();
        Console.WriteLine(msg);
        Chat.PrintToChat(caller, msg);
    }

    [Command("br_crates", Description = "Dev: print the crate-spawn convar values (front-load diagnostic).")]
    public void CmdCrates(CCitadelPlayerController caller)
    {
        string[] names =
        {
            "citadel_crate_spawn_enabled", "citadel_crate_spawn_initial_delay",
            "citadel_crate_early_to_trooper_spawn_delay", "citadel_crate_respawn_interval",
            "citadel_breakable_prop_initial_spawn_time_override",
        };
        Console.WriteLine("[Boss Rush] crate convars:");
        foreach (var nm in names)
            Console.WriteLine($"  {nm} = {ConVar.Find(nm)?.GetString() ?? "<not found>"}");
        Chat.PrintToChat(caller, "[Boss Rush] crate convars printed to console.");
    }

    [Command("br_loot", Description = "Dev: grant N random loot items to yourself (bypasses drop chance). e.g. br_loot [count=1]")]
    public void CmdLoot(CCitadelPlayerController caller, string count = "1")
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null) return;
        int n = int.TryParse(count, out var c) && c > 0 ? c : 1;
        for (int i = 0; i < n; i++) _loot.GrantLoot(pawn);
        var msg = $"[Boss Rush] granted {n} loot roll(s). pool={_loot.PoolSize} ({_loot.TierSummary})";
        Console.WriteLine(msg);
        Chat.PrintToChat(caller, msg);
    }

    [Command("br_bosscd", Description = "List + zero the boss's ability cooldowns (dev) — does it then attack?")]
    public void CmdBossCd(CCitadelPlayerController? caller = null)
    {
        var msg = $"[Boss Rush] {_patron.DebugAbilities()}";
        Console.WriteLine(msg);
        if (caller != null) Chat.PrintToChat(caller, msg);
    }
}
