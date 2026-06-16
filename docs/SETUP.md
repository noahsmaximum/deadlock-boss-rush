# Setup — building & running Boss Rush

Two toolchains, matching the two layers in `DESIGN.md`. The **server plugin** is what you
iterate on day-to-day; the **client addon** only needs touching for HUD/audio/map work.

> Everything here targets **Windows** with a Deadlock install. The community tools are
> Windows-first and Deadworks builds against the Source 2 game binaries.

---

## A. Server plugin (Deadworks C# plugin) — the main loop

### Prerequisites
- **Deadlock** installed via Steam.
- **Visual Studio 2026** with the *.NET* and *Desktop C++* workloads.
- **.NET 10 SDK** — <https://dotnet.microsoft.com/download/dotnet/10.0>.
- **protobuf 3.21.8** (headers + static lib) — only needed to build Deadworks itself.
- A local clone of **Deadworks**, built from source (no prebuilt binaries are distributed):
  <https://github.com/Deadworks-net/deadworks>.

### Build the SDK once
```sh
git clone --recursive https://github.com/Deadworks-net/deadworks
# follow its README to build the launcher + managed SDK (DeadworksManaged.Api)
```

### Build this plugin
Point the plugin at your SDK checkout (the project reads `$(DeadworksSdk)`):
```sh
# from repo root
dotnet build server/BossRush/BossRush.csproj -c Release -p:DeadworksSdk=C:/path/to/deadworks/managed/DeadworksManaged.Api
```
Output is a single `BossRush.dll`.

### Run it
1. Start a Deadworks dedicated server (from the Deadlock `bin` dir per the Deadworks README —
   it defaults to `-dedicated -insecure`).
2. Drop `BossRush.dll` into the server's **`plugins/`** directory. The loader auto-discovers
   any concrete `IDeadworksPlugin`/`DeadworksPluginBase` and **hot-reloads** on `.dll` change
   (≈500 ms debounce), so rebuilds reload live.
3. In Deadlock's console (F7): `connect localhost:27067`.

> ⚠️ The Deadworks API is *"early development, changes without notice."* If a symbol in
> `server/BossRush/**` doesn't resolve, check it against your local
> `managed/DeadworksManaged.Api` and update — the code here is provisional until P0 pins it.

---

## B. Client addon (VPK) — HUD, audio, map

Only needed for the relics HUD panel, rage-wave audio, custom props, or map edits.

### Prerequisites
- **CSDK 12** ("Community Source Development Kit") — the community Source 2 toolchain
  (Hammer, Asset Browser, Material/Model/Particle editors, Resource Compiler, VPK packer).
  Install per <https://deadlockmodding.pages.dev/modding-tools/csdk-12>.
- **Source 2 Viewer** (ValveResourceFormat) to decompile shipped UI/audio/VData for reference:
  <https://github.com/ValveResourceFormat/ValveResourceFormat>.

### Workflow
1. Author sources under `client/` (Panorama `*.xml`/`*.vcss`/`*.vjs`, soundevents
   `*.vsndevts`, optional `vdata/*.kv3`, maps `*.vmap`).
2. Compile with the CSDK 12 Resource Compiler.
3. Pack the compiled `game` tree into a VPK (CS2 Workshop Manager ≤2 GB, or Multichunk for
   larger; or the open-source **DeadPacker**).
4. Install: place `pak##_dir.vpk` in `Deadlock/game/citadel/addons/` (create it if missing;
   **lower number = higher priority**) and ensure `game/citadel/gameinfo.gi` has
   `Game citadel/addons` **above** `Game citadel` in `SearchPaths`.
   - The `gameinfo.gi` edit **resets on every major Deadlock patch** — re-apply it (mod
     managers automate this).
5. **Distribution:** the Deadworks launcher can auto-verify/download/decompress the addon to
   each client on connect — so onboarding becomes "use the launcher," not "manually copy
   VPKs." Every player still needs the matching addon for any client-side feature.

---

## C. Quick reference

| Task | Command / location |
|---|---|
| Build plugin | `dotnet build server/BossRush/BossRush.csproj -c Release -p:DeadworksSdk=…` |
| Deploy plugin | copy `BossRush.dll` → server `plugins/` (hot-reloads) |
| Join server | console (F7): `connect localhost:27067` |
| Client addon install | `Deadlock/game/citadel/addons/pak##_dir.vpk` + `gameinfo.gi` search path |

See `docs/DESIGN.md` for the full spec and roadmap.
