using DeadworksManaged.Api; // ⚠️ provisional — verify in P0

namespace BossRush;

/// <summary>
/// DESIGN.md #12 — every <see cref="BossRushConfig.RageWaveIntervalMinutes"/> minutes, flood
/// the map with <see cref="BossRushConfig.RageWaveTrooperMultiplier"/>× troopers on each side,
/// announce it (HUD + custom sound), and hold the "rage" state until every spawned wave
/// entity is dead ("until cleared"). The custom audio asset ships in the client addon.
/// </summary>
public sealed class RageWaveSystem
{
    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;
    private IHandle? _loop;
    private readonly List<CBaseEntity> _activeWave = new();
    public bool RageActive { get; private set; }

    public RageWaveSystem(BossRushConfig cfg, ITimer timer)
    {
        _cfg = cfg;
        _timer = timer;
    }

    public void Start()
    {
        _loop?.Cancel();
        _loop = _timer.Every(_cfg.RageWaveIntervalMinutes.Minutes(), TriggerWave);
    }

    public void Stop()
    {
        _loop?.Cancel();
        _loop = null;
        _activeWave.Clear();
        RageActive = false;
    }

    private void TriggerWave()
    {
        if (RageActive) return; // don't stack waves; skip if the last one isn't cleared yet
        RageActive = true;

        Announce("RAGE WAVE", "The map is overrun — clear it to break the surge!");
        PlayRageSound();

        // TODO(P4): spawn RageWaveTrooperMultiplier× troopers per side. Reuse SpawnDirector's
        // trooper-spawn helper (correct classname + lane spawn points discovered in P1).
        //   foreach (var point in TrooperSpawnPoints())
        //     for (int i = 0; i < BaseCount * _cfg.RageWaveTrooperMultiplier; i++)
        //       _activeWave.Add(SpawnTrooper(point));

        // Poll for "cleared" rather than tracking every death event.
        _timer.Every(2.Seconds(), CheckCleared);
    }

    private void CheckCleared()
    {
        _activeWave.RemoveAll(e => e is null || !e.IsValid); // ⚠️ confirm IsValid member name
        if (_activeWave.Count == 0 && RageActive)
        {
            RageActive = false;
            Announce("Wave cleared", "The surge subsides… for now.");
        }
    }

    private void PlayRageSound()
    {
        // EmitSound is on entities; emit from each player pawn (or a world entity) so every
        // client hears the client-addon soundevent. TODO(P4): pick the emitter.
        // somePawn.EmitSound(_cfg.RageWaveStartSound);
    }

    private static void Announce(string title, string desc) =>
        NetMessages.Send(
            new CCitadelUserMsg_HudGameAnnouncement { TitleLocstring = title, DescriptionLocstring = desc },
            RecipientFilter.All);
}
