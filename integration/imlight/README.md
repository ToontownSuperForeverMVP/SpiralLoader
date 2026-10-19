# Imlight integration

SpiralLoader integrates with [Imlight](https://github.com/Revive101/Imlight) through a maintained **patch
series** applied with `git am`, rather than runtime IL patching or source string replacement. Patches carry
context and blob ids, fail loudly when upstream moves, and can be rebased with Git's normal tools.

Pinned base: `b07987168fcfa2d123a5d78df90ca0c76405e427` (the commit Spiral Shuffle builds on). The series also
applies cleanly to `2a16010e59b95cc449d8e21542fbf38440e06bcf` (`quality-assurance`, 2026-10-16).

## Patches

| # | Commit | What it changes |
| --- | --- | --- |
| 0001 | `build: reference SpiralLoader from CoreLib and Director` | `src/SpiralLoader.props` (locates SpiralLoader), project references, git-commit stamping of the Director |
| 0002 | `feat: load SpiralLoader mods at startup` | `Imlight.CoreLib/Modding/ImlightModding.cs`, `ImlightEvents.cs`; Director starts the loader after configuration and publishes lifecycle events; `[SpiralLoader]` section in `Imlight.ini` |
| 0003 | `feat: let mods add game and login message services` | `GameServiceFactory`, `LoginServiceFactory` |
| 0004 | `feat: publish LootGrantingEvent before loot is granted` | `LootGranter.GrantAndDisplay` |
| 0005 | `feat: route unknown chat commands to mod commands` | `CommandDispatcher` fallback |
| 0006 | `fix: tolerate unloadable types when scanning assemblies` | `AssemblyTypes.LoadableTypes` used by the requirement, result and zone component registries |

Dependency direction: `Imlight.CoreLib → SpiralLoader.Api` (no third-party dependencies) and
`Imlight.Director → SpiralLoader.Imlight → SpiralLoader.Runtime`. CoreLib never references the runtime.

Behaviour without mods: `ImlightModding.Events` is `NullEventBus` until the Director attaches the loader; with
no `mods/` folder (or `Enabled = false`) every call site is a single `HasSubscribers` check returning false and
every service factory returns its built-in set unchanged.

## Scripts

| Script | Purpose |
| --- | --- |
| `setup-imlight.sh` / `Setup-Imlight.ps1` | Clone Imlight into `external/Imlight` (or a given path) at the pinned commit (override with `IMLIGHT_COMMIT` / `-Commit`), init submodules, apply the series on branch `spiralloader` |
| `verify/verify-patch.sh` / `verify/Verify-Patch.ps1` | Apply the series to a fresh checkout and boot the real patched Director against stubs (see `docs/building.md`) |

## Verification status

| Part | How verified |
| --- | --- |
| Series applies to b0798716 and 2a16010e | `git am` in the verification scripts |
| `Program.cs`, `ImlightModding.cs`, `ImlightEvents.cs`, `AssemblyTypes.cs`, `GameServiceFactory.cs`, `LoginServiceFactory.cs`, `ServiceFactory.cs`, `ZoneEntityComponentRegistry.cs` | Compiled (real patched files) and executed by the harness |
| Commit stamping targets in `Imlight.Director.csproj` | Exercised in isolation (git checkout, no git, explicit property) |
| `LootGranter.cs`, `CommandDispatcher.cs`, `RequirementDispatcher.cs`, `ResultDispatcher.cs` edits | Reviewed only: these files need Imcodec generated types. Requires a full local build |
| Whole `Imlight.CoreLib` / `Imlight.Director` build and a game client session | Not done: needs the Imcodec generator inputs and Wizard101 data |
