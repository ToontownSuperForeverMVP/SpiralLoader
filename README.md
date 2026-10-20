# SpiralLoader

A modloader for [Revive101's Imlight](https://github.com/Revive101/Imlight) Wizard101 private-server framework,
in the spirit of Fabric for Minecraft: a small loader, a stable mod API, and a maintained set of hook points in
the server. Mods are separately compiled .NET 10 assemblies plus data, described by a `spiralmod.json`
manifest and loaded once at server start.

> **Status: milestone 2 (integration API 1.2.0).** The loader, API, headless host, Imlight bootstrap and an
> Imlight patch series exist and are tested, including a public `ModMessageService` bridge through which a
> separately compiled mod receives its own client process replies. The first acceptance target, running
> [Spiral Shuffle](https://github.com/ljste/spiral-shuffle) as a standalone mod, still needs the hook points
> listed in [docs/next-milestone.md](docs/next-milestone.md).

## What works

- Mod discovery, manifest validation, SemVer dependency resolution (`depends`/`recommends`/`breaks`,
  `loadAfter`/`loadBefore`), deterministic load order, cycle detection, compatibility pins (Imlight commit,
  client revision), configurable failure policy.
- One `AssemblyLoadContext` per mod: host assemblies are shared, dependency mods share their types, private
  libraries stay private. Restart-required by design (no unloading).
- Typed event bus (priorities, cancellation, fault isolation, lock-free after startup), namespaced registries,
  rollback of a failed mod's registrations, lifecycle events, logging.
- Imlight hooks (patch series): loader start and lifecycle events, mod message services (`ModMessageService`,
  usable from a separately compiled mod), `LootGrantingEvent`, mod chat commands, `CharacterListSentEvent`,
  `GameServerSelectingEvent`, a client process job-id registry with per-mod reply routing, tolerant assembly
  scanning. Verified by booting the real patched Director, with Imlight's real `SessionActor` dispatch, against
  stubs on both the pinned and the current upstream Imlight commit.
- Asset declarations for client archives and SpiralDB, with deterministic overlay planning and conflict
  detection (writing archives is future work).

## Quick start

```powershell
dotnet build SpiralLoader.sln
dotnet test SpiralLoader.sln
dotnet run --project src/SpiralLoader.Host -- --mods artifacts/mods --run "hello world"
pwsh integration/imlight/verify/Verify-Patch.ps1     # or integration/imlight/verify/verify-patch.sh
```

## Layout

| Path | Contents |
| --- | --- |
| `src/SpiralLoader.Api` | Public mod API (no dependencies) |
| `src/SpiralLoader.Runtime` | Plugin manager |
| `src/SpiralLoader.Imlight` | Imlight bootstrap (no Imlight references) |
| `src/SpiralLoader.Host` | Headless development host |
| `examples/` | `HelloSpiral` and `HelloSpiral.Addon` example mods |
| `tests/SpiralLoader.Tests` | xUnit tests |
| `integration/imlight` | Patch series, setup scripts, verification harness |
| `build/SpiralLoader.Mod.props/.targets` | MSBuild support for mod projects |
| `docs/` | [Architecture](docs/architecture.md), [audit](docs/audit.md), [building](docs/building.md), [mod authoring](docs/mod-authoring.md), [next milestone](docs/next-milestone.md) |

## Important

- **Mods are trusted code.** Native mods run with full access to the server process; SpiralLoader validates
  and isolates mistakes but is not a sandbox.
- **No game data here.** SpiralLoader does not distribute Wizard101 assets, client files, or the
  client-derived inputs Imcodec needs. Building Imlight itself requires obtaining those yourself.
- Not affiliated with KingsIsle Entertainment or Revive101.

License: not chosen yet (no LICENSE file). Note that Imlight is AGPL-3.0 and the patch series in
`integration/imlight/patches` modifies it.
