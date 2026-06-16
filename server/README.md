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
  - `UpgradeStation` — enhance a held item for 2× price.
  - `RelicSystem` — power beyond the 16-slot cap, as modifiers (see `docs/DESIGN.md` §3).

## Build & run
```sh
dotnet build BossRush/BossRush.csproj -c Release \
  -p:DeadworksSdk=/path/to/deadworks/managed/DeadworksManaged.Api
# copy bin/Release/net10.0/BossRush.dll → your Deadworks server's plugins/ (hot-reloads)
```
Full steps + prerequisites: [`../docs/SETUP.md`](../docs/SETUP.md).

> ⚠️ **Provisional code.** The SDK is early-development and the signatures here came from
> reading the public source, not a compile. Treat `using DeadworksManaged.Api;`, method names,
> and types as *to-be-verified in roadmap phase P0*. The structure is real; the exact calls
> may need adjusting against your SDK clone. `TODO(Pn)` tags point at the roadmap phase that
> fills each gap.
