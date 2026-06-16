using DeadworksManaged.Api; // ⚠️ provisional — verify in P0

namespace BossRush;

/// <summary>
/// DESIGN.md #7 &amp; §3 — the engine caps a hero at 16 real item slots and there's no way to add
/// a functional 17th, client- or server-side. So "unlimited items" is modeled as **Relics**:
/// once a player fills their real slots, further pickups become server-tracked entries whose
/// effect is applied as a *modifier* rather than an item. Relics are shown in a custom HUD list
/// panel that ships in the client addon (the one unavoidable client-side piece of the economy).
///
/// This class also owns the timed *enhancements* (DESIGN.md #8): 5 min or until death.
/// </summary>
public sealed class RelicSystem
{
    private readonly BossRushConfig _cfg;
    private readonly ITimer _timer;

    // Per-player relic collections, keyed by the player's stable id (slot/SteamID — TBD in P2).
    private readonly Dictionary<int, List<string>> _relics = new();

    public RelicSystem(BossRushConfig cfg, ITimer timer)
    {
        _cfg = cfg;
        _timer = timer;
    }

    /// <summary>How many *real* Deadlock item slots a pawn is using (vs. relics).</summary>
    public int RealItemCount(CCitadelPlayerPawn pawn)
    {
        // TODO(P2): read the pawn's actual filled-slot count. Until then, assume not full so
        // early testing exercises AddItem rather than the relic path.
        return 0;
    }

    /// <summary>Grant a relic: record it and apply its effect as a modifier.</summary>
    public void GrantRelic(CCitadelPlayerPawn pawn, string itemName, bool enhanced)
    {
        var id = PlayerId(pawn);
        if (!_relics.TryGetValue(id, out var list))
            _relics[id] = list = new List<string>();
        list.Add(itemName);

        // TODO(P2): map item → an equivalent modifier (or a generic "relic" modifier whose
        // KeyValues3 carry the granted stats), then pawn.AddModifier(...).
        // TODO(P4): push the updated relic list to the client HUD panel (NetMessages / a
        //           shared game-state field the Panorama JS reads).
        _ = enhanced;
    }

    /// <summary>
    /// Apply a *timed* enhancement (5 min or until death). Bake the duration into the modifier
    /// KV and also clear it on death via <see cref="OnPlayerDeath"/>.
    /// </summary>
    public void ApplyTimedEnhancement(CCitadelPlayerPawn pawn, string modifierName)
    {
        // var kv = new KeyValues3();
        // kv.SetFloat("duration", _cfg.EnhancementDurationSeconds);
        // pawn.AddModifier(modifierName, kv);
    }

    /// <summary>On death, strip timed enhancements (relics persist).</summary>
    public void OnPlayerDeath(GameEvent e)
    {
        // TODO(P2): resolve the dying pawn from the event, then RemoveModifier for each active
        // timed enhancement it holds.
    }

    private static int PlayerId(CCitadelPlayerPawn pawn) => pawn.GetHashCode(); // TODO(P2): stable id
}
