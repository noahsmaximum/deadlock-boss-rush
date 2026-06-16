# Setup — building & running Boss Rush

Two toolchains, matching the two layers in `DESIGN.md`. **§A (server plugin)** is the
day-to-day loop and the only thing you need to start playtesting; **§B (client addon)** is
only for HUD/audio/map work. Everything here is **Windows** with a Deadlock install.

The steps below are verified against the real Deadworks repo (build chain, `local.props`
fields, plugin deploy path). Paths use `C:\...` placeholders — substitute your own.

---

## A. Server plugin (Deadworks) — the main loop

### A1 · Prerequisites
1. **Deadlock** (Steam). Note the bin path:
   `C:\Program Files (x86)\Steam\steamapps\common\Deadlock\game\bin\win64`
2. **Visual Studio 2026** with workloads: **Desktop development with C++** *and*
   **.NET desktop development**.
3. **.NET 10 SDK** — <https://dotnet.microsoft.com/en-us/download/dotnet/10.0>. Then find the
   `nethost` native dir (note the exact version folder):
   ```cmd
   dir "%ProgramFiles%\dotnet\packs\Microsoft.NETCore.App.Host.win-x64"
   ```
   → `C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x64\<ver>\runtimes\win-x64\native`
4. **CMake** + **git** on PATH (for building protobuf).

### A2 · Build protobuf 3.21.8 (the native layer statically links it)
```cmd
git clone --branch v3.21.8 --depth 1 https://github.com/protocolbuffers/protobuf.git C:\protobuf-3.21.8
cd C:\protobuf-3.21.8
cmake -B build -DCMAKE_BUILD_TYPE=Release -Dprotobuf_BUILD_TESTS=OFF -Dprotobuf_MSVC_STATIC_RUNTIME=ON
cmake --build build --config Release
```
Result: `C:\protobuf-3.21.8\build\Release\libprotobuf.lib`. Keep two paths handy: `src\`
(headers) and `build\Release\` (lib). *This is the most failure-prone step — if CMake errors,
copy the message here and we'll sort it.*

### A3 · Clone & build Deadworks
```cmd
git clone --recurse-submodules https://github.com/Deadworks-net/deadworks.git C:\deadworks
cd C:\deadworks
copy local.props.example local.props
```
Edit `C:\deadworks\local.props`:
```xml
<ProtobufIncludeDir>C:\protobuf-3.21.8\src</ProtobufIncludeDir>
<ProtobufLibDir>C:\protobuf-3.21.8\build\Release</ProtobufLibDir>
<NetHostDir>C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x64\<ver>\runtimes\win-x64\native</NetHostDir>
<DeadlockDir>C:\Program Files (x86)\Steam\steamapps\common\Deadlock\game\bin\win64</DeadlockDir>
```
Open `C:\deadworks\deadworks.slnx` in VS 2026, set **x64 / Release**, **Build Solution**.

### A4 · Link the Boss Rush plugin into the build
Our plugin source stays in *this* repo; junction it into Deadworks' examples so it builds &
auto-deploys exactly like the shipped plugins (`BossRush.csproj` already matches that pattern):
```cmd
mklink /J C:\deadworks\examples\plugins\BossRush C:\path\to\deadlock-boss-rush\server\BossRush
```
> A `/J` junction needs no admin and edits flow both ways — your repo remains the source of
> truth; Deadworks just compiles it in place.

Build the plugin:
```cmd
dotnet build C:\deadworks\examples\plugins\BossRush\BossRush.csproj -c Release
```
On success it copies `BossRush.dll` → `<DeadlockDir>\managed\plugins\` (via the `DeployToGame`
target + `DeadlockManagedDir`, which examples inherit from your `local.props`).

### A5 · Run & connect
1. Run **`deadworks.exe`** from `<DeadlockDir>` (`...\game\bin\win64\`). Watch its console for
   `[Boss Rush] loaded. Loot the lanes. Kill the Patron.`
2. Launch Deadlock, open console (`` ` `` / F7), and `connect localhost:27067`.
3. Edit any `.cs`, rebuild → the DLL **hot-reloads** (`[Boss Rush] reloaded.`) — no restart.

### A6 · P0 live experiments
With a server up, work through **`docs/VERIFIED_API.md` §9** — especially (1) how to enable the
**Street Brawl** uncapped-items ruleset, and (2) runtime-spawning an `npc_trooper`. The
`!upgrade` command is already wired; the example **DumperPlugin** + an `addspawn`-style
`[Command]` (see TagPlugin) help you dump entities and record coordinates in-game.

> ⚠️ The Deadworks API is *"early development, changes without notice."* The scaffold is written
> against source-verified signatures (`docs/VERIFIED_API.md`) but isn't compiled yet; if a
> symbol in `server/BossRush/**` doesn't resolve, check it against
> `C:\deadworks\managed\DeadworksManaged.Api` and update.

---

## B. Client addon (VPK) — HUD, audio, map

Only needed for the optional owned-items HUD, rage-wave audio, custom props, or map edits.

- **CSDK 12** (community Source 2 toolchain: Hammer, Asset Browser, Resource Compiler, VPK
  packer) — <https://deadlockmodding.pages.dev/modding-tools/csdk-12>.
- **Source 2 Viewer** (ValveResourceFormat) to decompile shipped UI/audio/VData for reference —
  <https://github.com/ValveResourceFormat/ValveResourceFormat>.

Workflow: author sources under `client/` (Panorama `*.xml`/`*.vcss`/`*.vjs`, soundevents
`*.vsndevts`, optional `vdata/*.kv3`, maps `*.vmap`) → compile with the Resource Compiler →
pack the compiled `game` tree into a VPK (CS2 Workshop Manager / Multichunk / DeadPacker) →
place `pak##_dir.vpk` in `Deadlock\game\citadel\addons\` and add `Game citadel/addons` **above**
`Game citadel` in `gameinfo.gi` (this edit **resets every major patch**). The Deadworks launcher
can auto-deliver the addon to clients on connect.

---

## C. Quick reference

| Task | Command / location |
|---|---|
| Build protobuf | `cmake -B build -DCMAKE_BUILD_TYPE=Release -Dprotobuf_BUILD_TESTS=OFF -Dprotobuf_MSVC_STATIC_RUNTIME=ON` then `cmake --build build --config Release` |
| Build Deadworks | open `deadworks.slnx`, x64/Release, Build Solution |
| Link plugin | `mklink /J C:\deadworks\examples\plugins\BossRush <repo>\server\BossRush` |
| Build plugin | `dotnet build C:\deadworks\examples\plugins\BossRush\BossRush.csproj -c Release` |
| Plugin deploys to | `<DeadlockDir>\managed\plugins\BossRush.dll` (auto) |
| Run server | `deadworks.exe` in `<DeadlockDir>` |
| Join | console (F7): `connect localhost:27067` |

See `docs/DESIGN.md` for the spec/roadmap and `docs/VERIFIED_API.md` for the API + P0 experiments.
