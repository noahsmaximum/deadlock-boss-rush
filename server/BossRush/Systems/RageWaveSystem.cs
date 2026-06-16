using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md #12 — every <see cref="BossRushConfig.RageWaveIntervalMinutes"/> minutes, flood the
/// map with <see cref="BossRushConfig.RageWaveTrooperMultiplier"/>× troopers on each side,
/// announce it (HUD + a custom sound shipped in the client addon), and hold the "rage" state
/// until every spawned wave entity is dead ("until cleared").
/// </summary>
public sealed class RageWaveSystem
{
    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;
    private IHandle? _loop;
    private IHandle? _clearPoll;
    private readonly List<uint> _waveHandles = new(); // entity handles of spawned wave troopers
    public bool RageActive { get; private set; }

    public RageWaveSystem(BossRushConfig cfg, ITimer timer)
    {
        _cfg = cfg;
        _timer = timer;
    }

    public void Start()
    {
        _loop?.Cancel();
        _loop = _timer.Every(((int)(_cfg.RageWaveIntervalMinutes * 60)).Seconds(), TriggerWave);
    }

    public void Stop()
    {
        _loop?.Cancel(); _loop = null;
        _clearPoll?.Cancel(); _clearPoll = null;
        _waveHandles.Clear();
        RageActive = false;
    }

    private void TriggerWave()
    {
        if (RageActive) return; // don't stack; wait for the previous surge to clear
        RageActive = true;

        Announce("RAGE WAVE", "The map is overrun — clear it to break the surge!");
        PlayRageSoundForEveryone();

        _waveHandles.Clear();
        // TODO(P4): spawn RageWaveTrooperMultiplier× npc_trooper per side at lane spawn points,
        // recording each entity handle so CheckCleared() can tell when the surge is gone:
        //   var t = CBaseEntity.CreateByName("npc_trooper");
        //   t.Teleport(position: point); t.TeamNum = team; t.Spawn();
        //   _waveHandles.Add(t.EntityHandle);

        _clearPoll?.Cancel();
        _clearPoll = _timer.Every(2.Seconds(), CheckCleared);
    }

    private void CheckCleared()
    {
        _waveHandles.RemoveAll(h =>
        {
            var e = CBaseEntity.FromHandle(h);
            return e == null || !e.IsAlive;
        });

        if (_waveHandles.Count == 0 && RageActive)
        {
            RageActive = false;
            _clearPoll?.Cancel(); _clearPoll = null;
            Announce("Wave cleared", "The surge subsides… for now.");
        }
    }

    private static void PlayRageSoundForEveryone()
    {
        // Emit the client-addon soundevent from each hero pawn so every player hears it.
        foreach (var pawn in Players.GetAllPawns())
            pawn.EmitSound("bossrush.ragewave.start"); // matches client/soundevents
    }

    private static void Announce(string title, string desc) =>
        NetMessages.Send(
            new CCitadelUserMsg_HudGameAnnouncement { TitleLocstring = title, DescriptionLocstring = desc },
            RecipientFilter.All);
}
