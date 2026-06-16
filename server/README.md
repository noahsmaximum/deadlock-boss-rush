# server/ — Deadworks plugin (gameplay)

The "brain" of Boss Rush: a single C# plugin (`BossRush.dll`) for the
[Deadworks](https://github.com/Deadworks-net/deadworks) server framework. Everything here is
**server-authoritative — no player install required.**

## Layout
- `BossRush/BossRushPlugin.cs` — game-mode entry; lifecycle + event hooks; wires up systems.
- `BossRush/BossRushConfig.cs` — every tunable, exposed via `[PluginConfig]`.
- `BossRush/Systems/` — one file per mechanic:
  - `SpawnDirector` — 2× enemy troopers; stronger, time-scaling denizens.
  - `RageWaveSystem` — 10-min 4× waves, held "until cleared", audio cue.
  - `PatronCombatSystem` — Patron lasers (damage + particle + sound), scaling, random buffs.
  - `LootSystem` — gold-statue/crate pickups → `AddItem`; rare enhanced drops.
  - `UpgradeStation` — enhance a held item for 2× price (`!upgrade <item>`).
  - `EnhancementSystem` — temporary enhanced items (5 min / until death); items themselves are
    uncapped under the Street Brawl ruleset (see `docs/DESIGN.md` §3).

## Build & run
```sh
dotnet build BossRush/BossRush.csproj -c Release \
  -p:DeadworksSdk=/path/to/deadworks/managed/DeadworksManaged.Api
# copy bin/Release/net10.0/BossRush.dll → your Deadworks server's plugins/ (hot-reloads)
```
Full steps + prerequisites: [`../docs/SETUP.md`](../docs/SETUP.md).

> ⚠️ **Written against source-verified APIs, not yet compiled.** Signatures, event types, NPC
> classnames, convars, and currency enums were pinned by reading the actual Deadworks source
> (see `docs/VERIFIED_API.md`) — but the SDK is early-development ("APIs change without notice")
> and this hasn't been built against your local SDK yet. `TODO(P0)` marks the few things that
> still need a *running server* to confirm (e.g. runtime NPC spawning, EntityTouch member names,
> enabling the Street Brawl uncapped-items ruleset). `TODO(Pn)` tags map to roadmap phases.
