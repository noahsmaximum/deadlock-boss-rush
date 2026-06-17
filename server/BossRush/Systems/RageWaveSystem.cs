using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// DESIGN.md #12 — every <see cref="BossRushConfig.RageWaveIntervalMinutes"/> minutes the Hidden
/// King floods its lanes: for the surge we burst extra Hidden King troopers down the lanes (via the
/// zipline spawner) every <see cref="BossRushConfig.RageWaveSpawnIntervalSeconds"/>, announce it, and
/// stop after <see cref="BossRushConfig.RageWaveSurgeDurationSeconds"/>.
///
/// Squad size per burst starts at <see cref="BossRushConfig.RageWaveSquadBase"/> (10) and steps up by
/// <see cref="BossRushConfig.RageWaveSquadStep"/> (5) at minute
/// <see cref="BossRushConfig.RageWaveSquadFirstStepMinute"/> (20), then every
/// <see cref="BossRushConfig.RageWaveSquadStepMinutes"/> (10) → 10 / 15@20m / 20@30m / 25@40m …
/// </summary>
public sealed class RageWaveSystem
{
    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;
    private IHandle? _loop;
    private IHandle? _surgeBurst;
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
        _surgeBurst?.Cancel(); _surgeBurst = null;
        _surgeEnd?.Cancel(); _surgeEnd = null;
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

        SpawnBurst(); // immediate, then repeat through the surge
        _surgeBurst?.Cancel();
        _surgeBurst = _timer.Every(((int)_cfg.RageWaveSpawnIntervalSeconds).Seconds(), SpawnBurst);

        _surgeEnd?.Cancel();
        _surgeEnd = _timer.Once(((int)_cfg.RageWaveSurgeDurationSeconds).Seconds(), EndWave);
    }

    private void EndWave()
    {
        if (!RageActive) return;
        RageActive = false;
        _surgeBurst?.Cancel(); _surgeBurst = null;
        _surgeEnd?.Cancel(); _surgeEnd = null;
        Announce("Wave cleared", "The surge subsides… for now.");
    }

    private void SpawnBurst() =>
        LaneTroopers.Spawn(BossRushPlugin.EnemyTeam, SquadSizeForNow(), _cfg.HiddenKingLanesCsv);

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
