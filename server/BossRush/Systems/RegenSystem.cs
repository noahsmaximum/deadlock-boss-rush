using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// Custom hero health regen. Deadlock's <c>sv_regeneration_force_on</c> cheat has no rate cvar
/// (only <c>force_on</c> + <c>wait_time</c>), so its fast rate can't be slowed — instead we heal the
/// hero pawns ourselves at a controlled, configurable rate. Hero-only sustain (the Hidden King's
/// horde never regenerates), and it pauses for a moment after a hero takes damage.
/// </summary>
public sealed class RegenSystem
{
    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;
    private IHandle? _loop;
    private readonly EntityData<float> _lastDamage = new();

    public RegenSystem(BossRushConfig cfg, ITimer timer)
    {
        _cfg = cfg;
        _timer = timer;
    }

    public void Start()
    {
        _loop?.Cancel();
        _loop = _timer.Every(1.Seconds(), Tick);
    }

    public void Stop()
    {
        _loop?.Cancel();
        _loop = null;
        _lastDamage.Clear();
    }

    /// <summary>Record a hero taking damage so regen pauses for <see cref="BossRushConfig.RegenWaitSeconds"/>.</summary>
    public void OnHeroDamaged(CCitadelPlayerPawn pawn) => _lastDamage[pawn] = GameRules.GameClock;

    private void Tick()
    {
        if (_cfg.RegenPerSecond <= 0f) return;

        float now = GameRules.GameClock;
        foreach (var pawn in Players.GetAllPawns())
        {
            if (!pawn.IsAlive) continue;
            if (_lastDamage.TryGet(pawn, out var last) && now - last < _cfg.RegenWaitSeconds) continue;
            pawn.Heal(_cfg.RegenPerSecond);
        }
    }
}
