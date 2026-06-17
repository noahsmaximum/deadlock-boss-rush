# Deadlock BOSS RUSH

A co-op **PvE** mode for Valve's *Deadlock*. Your team spawns on one side of the map and
fights through three lanes — defended by **double the Guardians** and a multi-phase **Hidden
King** (the Patron) that fights back with scaling lasers and **hero ultimates** — looting power
from the world (golden buddhas, boxes, crystals) instead of the shop. Only **legendary items**
stay buyable, at a steep markup. One goal: **topple the Hidden King.**

Signature beats: an **Upgrade Station** that enhances the items you already hold (at 2× cost),
**2× enemy trooper spawns**, **scaling denizens**, extra **crystal-buff** spawns, and a
**rage wave every 10 minutes** that floods the map with 4× troops until cleared.

## How it's built

Two coordinated mods (Deadlock has no official custom-games SDK):

- **`server/`** — a C# plugin for the **[Deadworks](https://github.com/Deadworks-net/deadworks)**
  server framework. Runs all gameplay logic on a custom dedicated server — **no player
  install required.**
- **`client/`** — a Source 2 **VPK** addon (built with CSDK 12) for presentation only:
  the relics/enhancements HUD panel, rage-wave audio, and any custom props/map edits.
  **Every player installs this** (auto-delivered by the Deadworks launcher on connect).

## Start here

- **[`docs/DESIGN.md`](docs/DESIGN.md)** — full spec, a feasibility verdict for every
  mechanic, the architecture, the phased roadmap, and the open decisions.
- **[`docs/VERIFIED_API.md`](docs/VERIFIED_API.md)** — source-verified Deadworks API reference
  (signatures, event types, NPC classnames, convars) + the open runtime experiments.
- **[`docs/SETUP.md`](docs/SETUP.md)** — dev environment, build, run, and packaging.

> ⚠️ Built on **unofficial, early-development** community tooling. Deadworks' APIs change
> without notice; CSDK/`gameinfo.gi` break on Deadlock patches. The `server/` scaffold is
> written against **source-verified signatures** and now **runs live** on a dedicated server (core
> systems confirmed 2026-06-16; a few things still need live tuning — see VERIFIED_API.md §9).
> Custom servers run outside Valve matchmaking; no official modding policy exists.
