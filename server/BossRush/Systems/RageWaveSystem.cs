using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md #12 — every <see cref="BossRushConfig.RageWaveIntervalMinutes"/> minutes the Hidden
/// King floods the lanes: we temporarily crank the trooper wave cvars (squad size up, spawn
/// interval down) so the lane spawners pour troopers out for the surge, announce it, then restore
/// the normal cadence.
///
/// Squad size starts at <see cref="BossRushConfig.RageWaveSquadBase"/> (10) and steps up by
/// <see cref="BossRushConfig.RageWaveSquadStep"/> (5) at minute
/// <see cref="BossRushConfig.RageWaveSquadFirstStepMinute"/> (20), then again every
/// <see cref="BossRushConfig.RageWaveSquadStepMinutes"/> (10) → 10 / 15@20m / 20@30m / 25@40m …
/// </summary>
public sealed class RageWaveSystem
{
    // Game defaults to restore after a surge (citadel_trooper_*): squad 4, early/late 30/25s.
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
        if (RageActive) RestoreCadence();
        RageActive = false;
    }

    /// <summary>Fire a rage wave immediately (dev/testing via <c>dw_br_ragewave</c>).</summary>
    public void TriggerNow() => TriggerWave();

    private void TriggerWave()
    {
        if (RageActive) return; // don't stack; wait for the current surge to subside
        RageActive = true;

        Announce("RAGE WAVE", "The Hidden King floods the lanes — survive the surge!");
        PlayRageSoundForEveryone();

        SetCadence(SquadSizeForNow(), _cfg.RageWaveSpawnIntervalSeconds);

        _surgeEnd?.Cancel();
        _surgeEnd = _timer.Once(((int)_cfg.RageWaveSurgeDurationSeconds).Seconds(), EndWave);
    }

    private void EndWave()
    {
        if (!RageActive) return;
        RageActive = false;
        _surgeEnd?.Cancel(); _surgeEnd = null;
        RestoreCadence();
        Announce("Wave cleared", "The surge subsides… for now.");
    }

    /// <summary>10 to start; +step at the first-step minute and again every step interval after.</summary>
    private int SquadSizeForNow()
    {
        float minutes = GameRules.GameClock / 60f;
        int squad = _cfg.RageWaveSquadBase;
        if (minutes >= _cfg.RageWaveSquadFirstStepMinute)
        {
            int steps = 1 + (int)((minutes - _cfg.RageWaveSquadFirstStepMinute) / _cfg.RageWaveSquadStepMinutes);
            squad += _cfg.RageWaveSquadStep * steps;
        }
        return squad;
    }

    private static void SetCadence(int squad, float intervalSeconds)
    {
        ConVar.Find("citadel_trooper_squad_size")?.SetInt(squad);
        ConVar.Find("citadel_trooper_spawn_interval_early")?.SetFloat(intervalSeconds);
        ConVar.Find("citadel_trooper_spawn_interval_late")?.SetFloat(intervalSeconds);
    }

    private static void RestoreCadence()
    {
        ConVar.Find("citadel_trooper_squad_size")?.SetInt(BaseSquad);
        ConVar.Find("citadel_trooper_spawn_interval_early")?.SetFloat(BaseIntervalEarly);
        ConVar.Find("citadel_trooper_spawn_interval_late")?.SetFloat(BaseIntervalLate);
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
