using System.Numerics;
using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md #9, #10 — keep the Hidden King ahead of the Archmother. The natural lane waves spawn
/// equal troopers for both sides, so we pour extra Hidden King troopers down their lanes on a steady
/// reinforcement loop (~2× the Archmother), and buff each enemy trooper as it spawns so they get
/// deadlier with match time. Enemies come from the Hidden King's lanes via the zipline spawner
/// (raw <c>npc_trooper</c> spawns AV; see docs/VERIFIED_API.md §11–12).
/// </summary>
public sealed class SpawnDirector
{
    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;
    private IHandle? _reinforce;
    private IHandle? _guardianSweep;
    private bool _guardiansDoubled;

    public SpawnDirector(BossRushConfig cfg, ITimer timer)
    {
        _cfg = cfg;
        _timer = timer;
    }

    public void Start()
    {
        _reinforce?.Cancel();
        _reinforce = _timer.Every(((int)_cfg.ReinforcementIntervalSeconds).Seconds(), Reinforce);

        // Double the Guardians once, after the map-placed defenses have settled.
        _guardiansDoubled = false;
        _guardianSweep?.Cancel();
        if (_cfg.DoubleGuardians)
            _guardianSweep = _timer.Once(((int)(_cfg.GuardianDoubleDelaySeconds * 1000)).Milliseconds(),
                () => DoubleGuardiansNow());
    }

    public void Stop()
    {
        _reinforce?.Cancel();
        _reinforce = null;
        _guardianSweep?.Cancel();
        _guardianSweep = null;
    }

    /// <summary>Front tier-1 Guardians get a twin each (fight 2 instead of 1); base Guardians get one extra twin
    /// per lane so each lane reads 3 (matching the middle). Snapshots first so twins aren't re-twinned; runs once
    /// per map. EXPERIMENTAL — server-spawned guardians may not bind to the objective system; verify in-game.</summary>
    public string DoubleGuardiansNow()
    {
        // Raw-spawning npc_trooper_boss crashes the server (objective NPC, like troopers) — gate hard until a
        // map-edit path exists. Flipping DoubleGuardians on without that will crash on Spawn().
        if (!_cfg.DoubleGuardians)
            return "[Boss Rush] guardian reinforcement is OFF — server-side spawn of objective NPCs crashes; needs a map edit.";
        if (_guardiansDoubled) return "[Boss Rush] guardians already reinforced this map.";

        int extra = SpawnExtraGuardians();
        _guardiansDoubled = extra > 0;
        return $"[Boss Rush] placed {extra}/{_cfg.ExtraGuardianPositions.Length} extra front Guardians " +
               $"('{_cfg.FrontGuardianDesignerName}') at exact coords.";
    }

    /// <summary>Place one extra front Guardian at each configured exact world position (a second guardian beside an
    /// existing one). Health/team are copied from a live front Guardian when one exists.</summary>
    private int SpawnExtraGuardians()
    {
        var template = Entities.All.FirstOrDefault(e =>
            e.DesignerName == _cfg.FrontGuardianDesignerName
            && e.IsAlive && e.TeamNum == BossRushPlugin.EnemyTeam);
        int hp = template?.MaxHealth ?? 8000;

        int made = 0;
        foreach (var spec in _cfg.ExtraGuardianPositions)
        {
            if (!TryParseVec(spec, out var pos)) continue;
            var g = CBaseEntity.CreateByDesignerName(_cfg.FrontGuardianDesignerName);
            if (g == null) continue;
            g.TeamNum = BossRushPlugin.EnemyTeam;
            g.Teleport(position: pos);
            g.Spawn();
            g.MaxHealth = hp;
            g.Health = hp;
            made++;
        }
        return made;
    }

    private static bool TryParseVec(string s, out Vector3 v)
    {
        v = default;
        var p = s.Split(',');
        if (p.Length != 3) return false;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (float.TryParse(p[0], System.Globalization.NumberStyles.Float, ci, out var x)
         && float.TryParse(p[1], System.Globalization.NumberStyles.Float, ci, out var y)
         && float.TryParse(p[2], System.Globalization.NumberStyles.Float, ci, out var z))
        {
            v = new Vector3(x, y, z);
            return true;
        }
        return false;
    }

    /// <summary>Steady stream of extra Hidden King troopers down every lane (keeps them ~2× the Archmother).</summary>
    private void Reinforce() =>
        LaneTroopers.SpawnPerLane(BossRushPlugin.EnemyTeam, _cfg.ReinforcementSquadSize, _cfg.HiddenKingLanesCsv);

    /// <summary>Buff each enemy defense as it spawns: Walkers, front Guardians and base Guardians by their own flat
    /// HP multiplier; generic lane troopers time-scaled. The front Guardian is npc_trooper_boss — matched explicitly
    /// so it gets the guardian buff, not the generic "trooper" buff.</summary>
    public void OnEntitySpawned(EntitySpawnedEvent e)
    {
        string name = e.Entity.DesignerName;

        // Team and health are assigned a tick after the spawn event fires — defer the check + buff.
        uint handle = e.Entity.EntityHandle;
        _timer.NextTick(() =>
        {
            var t = CBaseEntity.FromHandle(handle);
            if (t == null || !t.IsAlive || t.MaxHealth <= 0) return;
            if (t.TeamNum != BossRushPlugin.EnemyTeam) return;

            float mult = MultiplierFor(name);
            if (mult <= 1f) return;

            t.MaxHealth = (int)(t.MaxHealth * mult);
            t.Health = t.MaxHealth;
        });
    }

    /// <summary>The on-spawn HP multiplier for an enemy defense by designer name.</summary>
    private float MultiplierFor(string designer)
    {
        if (designer == _cfg.WalkerDesignerName) return _cfg.WalkerHealthMultiplier;             // npc_boss_tier2
        if (designer == _cfg.FrontGuardianDesignerName) return _cfg.FrontGuardianHealthMultiplier; // npc_trooper_boss
        if (designer == _cfg.BaseGuardianDesignerName) return _cfg.BaseGuardianHealthMultiplier;    // npc_barrack_boss

        // Generic lane troopers (npc_trooper, …) but NOT the *_boss objective NPCs → time-scaled.
        if (designer.Contains("trooper", StringComparison.OrdinalIgnoreCase)
            && !designer.Contains("boss", StringComparison.OrdinalIgnoreCase))
        {
            float minutes = GameRules.GameClock / 60f;
            return _cfg.DenizenBaseStrengthMultiplier + _cfg.DenizenStrengthPerMinute * minutes;
        }
        return 1f;
    }
}
