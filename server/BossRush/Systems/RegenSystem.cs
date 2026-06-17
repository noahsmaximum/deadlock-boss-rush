using DeadworksManaged.Api;

namespace BossRush;

/// <summary>
/// Custom hero health regen. Deadlock's <c>sv_regeneration_force_on</c> cheat has no rate cvar, and
/// the HUD's regen stat is a computed value with no settable field — so we heal the hero pawns
/// ourselves. Rate ramps with match time from <see cref="BossRushConfig.RegenStartPerSecond"/> to
/// <see cref="BossRushConfig.RegenMaxPerSecond"/> over <see cref="BossRushConfig.RegenRampMinutes"/>.
/// Hero-only (the Hidden King's horde never regenerates); pauses briefly after a hero takes damage.
/// (The on-screen "regen" number won't reflect this — it's a separate computed stat.)
/// </summary>
public sealed class RegenSystem
{
    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;
    private IHandle? _loop;
    private int _ticks;
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

    /// <summary>Linear ramp from start to max rate over the configured minutes.</summary>
    private float CurrentRate()
    {
        if (_cfg.RegenRampMinutes <= 0f) return _cfg.RegenMaxPerSecond;
        float minutes = GameRules.GameClock / 60f;
        float t = Math.Clamp(minutes / _cfg.RegenRampMinutes, 0f, 1f);
        return _cfg.RegenStartPerSecond + (_cfg.RegenMaxPerSecond - _cfg.RegenStartPerSecond) * t;
    }

    private void Tick()
    {
        float rate = CurrentRate();
        if (rate <= 0f) return;

        float now = GameRules.GameClock;
        int healed = 0;
        foreach (var controller in Players.GetAll())
        {
            var pawn = controller.GetHeroPawn();
            if (pawn == null || !pawn.IsAlive) continue;
            if (pawn.Health >= pawn.MaxHealth) continue;
            if (_lastDamage.TryGet(pawn, out var last) && now - last < _cfg.RegenWaitSeconds) continue;

            pawn.Heal(rate);
            healed++;
        }

        // Throttled diagnostic (every ~5s). Remove once dialed in.
        if (++_ticks % 5 == 0)
            Console.WriteLine($"[Boss Rush] regen: healed {healed} hero(es) +{rate:F0}/s (clock {now:F0})");
    }
}
