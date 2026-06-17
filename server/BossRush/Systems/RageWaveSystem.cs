using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md #12 — every <see cref="BossRushConfig.RageWaveIntervalMinutes"/> minutes the Hidden
/// King floods every lane: we temporarily crank the trooper wave cvars (bigger squads, much shorter
/// interval) so the natural lane spawners pour troopers out for the surge, announce it (HUD + a
/// custom sound from the client addon), then restore the normal cadence.
/// </summary>
public sealed class RageWaveSystem
{
    private static readonly Duration SurgeDuration = 45.Seconds();

    // Game defaults (citadel_trooper_*): squad 4, early/late interval 30/25s.
    private const int BaseSquad = 4, BaseIntervalEarly = 30, BaseIntervalLate = 25;

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
        if (RageActive) SetTrooperWaves(BaseSquad, BaseIntervalEarly, BaseIntervalLate);
        RageActive = false;
    }

    /// <summary>Fire a rage wave immediately (dev/testing via <c>dw_br_ragewave</c>).</summary>
    public void TriggerNow() => TriggerWave();

    private void TriggerWave()
    {
        if (RageActive) return; // don't stack; wait for the current surge to subside
        RageActive = true;

        Announce("RAGE WAVE", "The Hidden King floods every lane — survive the surge!");
        PlayRageSoundForEveryone();

        // Flood the lanes: bigger, faster waves for the surge.
        int rageSquad = Math.Clamp((int)MathF.Round(BaseSquad * _cfg.RageWaveTrooperMultiplier), 8, 24);
        SetTrooperWaves(rageSquad, intervalEarly: 6, intervalLate: 6);

        _surgeEnd?.Cancel();
        _surgeEnd = _timer.Once(SurgeDuration, EndWave);
    }

    private void EndWave()
    {
        if (!RageActive) return;
        RageActive = false;
        _surgeEnd?.Cancel(); _surgeEnd = null;
        SetTrooperWaves(BaseSquad, BaseIntervalEarly, BaseIntervalLate);
        Announce("Wave cleared", "The surge subsides… for now.");
    }

    private static void SetTrooperWaves(int squad, int intervalEarly, int intervalLate)
    {
        ConVar.Find("citadel_trooper_squad_size")?.SetInt(squad);
        ConVar.Find("citadel_trooper_spawn_interval_early")?.SetFloat(intervalEarly);
        ConVar.Find("citadel_trooper_spawn_interval_late")?.SetFloat(intervalLate);
    }

    private void PlayRageSoundForEveryone()
    {
        foreach (var pawn in Players.GetAllPawns())
            pawn.EmitSound(_cfg.RageWaveStartSound);
    }

    private static void Announce(string title, string desc) =>
        NetMessages.Send(
            new CCitadelUserMsg_HudGameAnnouncement { TitleLocstring = title, DescriptionLocstring = desc },
            RecipientFilter.All);
}
