using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md #12 — every <see cref="BossRushConfig.RageWaveIntervalMinutes"/> minutes, flood the
/// map with hostile troopers, announce it (HUD + a custom sound shipped in the client addon), and
/// hold the "rage" state for the surge.
///
/// Troopers are spawned via the game's console spawner (<see cref="Troopers"/>); that gives us no
/// entity handles back, so the surge ends on a timer rather than true "until every wave entity is
/// dead" tracking. (Upgrade later by counting enemy troopers if we want exact clears.)
/// </summary>
public sealed class RageWaveSystem
{
    private static readonly Duration SurgeDuration = 45.Seconds();

    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;
    private IHandle? _loop;
    private IHandle? _surgeEnd;
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
        _surgeEnd?.Cancel(); _surgeEnd = null;
        RageActive = false;
    }

    /// <summary>Fire a rage wave immediately (dev/testing via <c>dw_br_ragewave</c>).</summary>
    public void TriggerNow() => TriggerWave();

    private void TriggerWave()
    {
        if (RageActive) return; // don't stack; wait for the current surge to subside
        RageActive = true;

        Announce("RAGE WAVE", "The map is overrun — survive the surge!");
        PlayRageSoundForEveryone();

        // RageWaveTrooperMultiplier sized as an NxN grid (4× -> 4x4 = 16 troopers).
        int grid = Math.Clamp((int)MathF.Round(_cfg.RageWaveTrooperMultiplier), 2, 6);
        Troopers.SpawnGrid(grid);

        _surgeEnd?.Cancel();
        _surgeEnd = _timer.Once(SurgeDuration, EndWave);
    }

    private void EndWave()
    {
        if (!RageActive) return;
        RageActive = false;
        _surgeEnd?.Cancel(); _surgeEnd = null;
        Announce("Wave cleared", "The surge subsides… for now.");
    }

    private void PlayRageSoundForEveryone()
    {
        // Emit the client-addon soundevent from each hero pawn so every player hears it.
        foreach (var pawn in Players.GetAllPawns())
            pawn.EmitSound(_cfg.RageWaveStartSound);
    }

    private static void Announce(string title, string desc) =>
        NetMessages.Send(
            new CCitadelUserMsg_HudGameAnnouncement { TitleLocstring = title, DescriptionLocstring = desc },
            RecipientFilter.All);
}
