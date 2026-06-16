# Deadlock Boss Rush

A co-op **PvE** mode for Valve's *Deadlock*. Your team spawns on one side of the map and
fights through three lanes — defended by **double the Guardians** and a **Patron that fights
back** with scaling laser attacks — looting power from the world (golden buddhas, boxes,
crystals) instead of buying from a shop, building toward one goal: **kill the Patron.**

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
- **[`docs/SETUP.md`](docs/SETUP.md)** — dev environment, build, run, and packaging.

> ⚠️ Built on **unofficial, early-development** community tooling. Deadworks' APIs change
> without notice; CSDK/`gameinfo.gi` break on Deadlock patches. The SDK signatures in
> `server/` are **provisional until verified against a local SDK clone** (roadmap phase P0).
> Custom servers run outside Valve matchmaking; no official modding policy exists.
