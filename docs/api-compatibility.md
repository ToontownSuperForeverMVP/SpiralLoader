# API compatibility contracts

This page says what a separately compiled mod may rely on, what counts as a compatible or a breaking change, and
how the project checks itself against its own promises. SpiralLoader is experimental software. Nothing here says
the API is frozen or production-stable: it says what the project tries to keep stable, and gives the tools to notice
when a change does not.

## Two version numbers

| Version | Where it comes from | What it describes | Mods depend on it as |
| --- | --- | --- | --- |
| **SpiralLoader API** `0.1.0` | `SpiralLoaderInfo.ApiVersion`; `SpiralLoader.Api.dll` assembly version `0.1.0.0` | The host-agnostic mod API: `ISpiralMod`, `IModContext`, events, registries, manifest model, versioning, commands | built-in id `spiralloader` |
| **Imlight integration API** `1.2.0` | `ImlightModding.IntegrationApiVersion` in the patch series | What the patched Imlight adds on top: `Imlight.CoreLib.Modding` events, registries, `ModMessageService` | built-in id `imlight` |

They move independently. Declare both in `depends` when you use both (see
[mod-authoring.md](mod-authoring.md#declaring-the-versions-you-need)). The assembly version of
`SpiralLoader.Api.dll` is a third, deliberate signal: it changes only for breaking API changes, so a mod built
against an older assembly version is flagged by the loader (`assembly.api_older`) and a mod built against a newer one
is refused before its code runs (`assembly.api_newer`).

## What is a compatibility contract

"Contract" means: a change that breaks it needs a version bump, a note in the changelog section below, and, for
the first four rows, a deliberate update of the recorded baseline. Everything not listed here is not a contract.

| # | Contract | Covers | Guarded by |
| --- | --- | --- | --- |
| 1 | **Public surface of `SpiralLoader.Api`** | Every public (and protected) type and member: signatures, parameter names and defaults, nullability annotations, constants, record shapes, enum values, and the *names* of registry keys and the version constant | `api-baseline/<version>/SpiralLoader.Api.txt`, `ApiCompatibilityTests` |
| 2 | **Imlight integration API: mod-facing surface** | `Imlight.CoreLib.Modding`: `ImlightRegistries` (keys and value types), `LootGrantingEvent`, `CharacterListSentEvent`, `GameServerSelectingEvent` (incl. `Cancel` semantics), `ModMessageService` (protected helpers, `OnSessionEnded`, `[MessageHandler]` discovery), `ImlightModding.IntegrationApiVersion`, `ImlightModding.IsClientProcessJobClaimed` | `api-baseline/<version>/Imlight.CoreLib.Modding.txt`, compared by both Imlight verify scripts on both supported revisions |
| 3 | **Diagnostic codes** | The code strings in `DiagnosticCodes` never change meaning or disappear; new failure modes get new codes (and a row in [architecture.md](architecture.md#diagnostics)). Message and hint *texts* are not contracts | `api-baseline/<version>/DiagnosticCodes.txt`, `ApiCompatibilityTests` |
| 4 | **Manifest schema 1** | Field names, types and meaning in `spiralmod.json`; id and version rules; the version-range grammar; "unknown fields warn, they do not fail" | `ManifestTests`, `VersioningTests` |
| 5 | **Runtime semantics mods rely on** | Event dispatch by exact type, ordered by priority, then owner load order, then registration; `CancellableEvent` and `Monitor` behaviour; handlers run synchronously on the publishing thread; registration is only accepted during `Initialize` and is frozen afterwards; registry entry ids are `modid:name`; `uniqueValues` registries reject a second claim in the second mod's `Initialize`; a failed `Initialize` rolls back that mod's subscriptions and registrations; dependency resolution is deterministic and cascades rejections; `spiralloader`, `dotnet` and `imlight` are host built-ins | `EventAndRegistryTests`, `LoadingTests`, `ResolutionTests`, the `ApiTour` fixture (below) |
| 6 | **Load and failure policy** | `Abort` is the default policy; disabled mods are skipped and their dependents rejected; no assembly unloading; mods load before Imlight scans assemblies | `ResolutionTests`, `ExternalModCompatibilityTests`, Imlight verify harness |

### Public but not for mods

These are public so the patched Director and the Imlight call sites can reach them. They are **not** mod contracts
and can change in any release (the Imlight baseline still records them, so the change is visible):
`ImlightModding.Attach`, `ImlightModding.Events` / `Registries` / `IsAttached`, `ImlightModding.Validate*`; all of
`SpiralLoader.Runtime` and `SpiralLoader.Imlight` (the host-integration side); the `SpiralLoader.Host` command line
(its exit codes `0`/`1`/`2` and the `--check` verdict line are meant to be stable for scripts, the rest of its output is
not).

### Explicitly not contracts

Log and report layout, message and hint wording, the order of unspecified things (for example which of two
requirement handlers for the same type wins), timing, load performance, the internals of the patch series, Imlight's
own behaviour, and anything marked "planned" in the docs.

## SemVer expectations

The rules below are how the project intends to number releases. They are a commitment to *practice*, not a
guarantee, and until `SpiralLoader.Api` reaches 1.0.0 the first column applies as SemVer 2.0.0 section 4 allows
("anything may change at any time").

### SpiralLoader API (`spiralloader`, `0.x`)

| Change | Version bump while `0.x` | After `1.0.0` |
| --- | --- | --- |
| Bug fix, documentation, clearer diagnostics text, new hint | patch | patch |
| New type, new member on a class or record, new optional manifest field, new event, new registry key, new diagnostic code, new optional parameter on a new overload | **minor** | minor |
| Remove or rename a public type or member; change a signature, return type, parameter type, parameter name, default value or nullability; add an abstract member to an interface; change an enum value; change a constant or registry key name | **minor, plus a changelog entry and a new `AssemblyVersion`** | major |
| Change event ordering, cancellation, freeze, rollback, resolution or manifest semantics so that a valid mod behaves differently | **minor, plus a changelog entry and a new `AssemblyVersion`** | major |
| Reject a manifest or mod that was valid before (stricter validation) | minor + changelog | major |
| Remove a diagnostic code or change what it means | not done: add a new code instead | not done |

Additions are safe for mods built against an older API only when the mod never needed them, so a mod that uses
something added in `0.2.0` must say so: `"spiralloader": ">=0.2.0 <0.3.0"`. Default interface members are the
preferred way to add behaviour to an interface mods implement (`ISpiralMod.Shutdown` is one).

### Imlight integration API (`imlight`, `1.x`)

| Change | Bump |
| --- | --- |
| Bug fix inside a hook, no change to its observable contract | none (patch series revision) |
| New event, registry, helper or hook; a new property on an existing event | minor |
| Remove or change a hook, event property, registry key or value type, `ModMessageService` helper or dispatch rule; change when or on which thread an event is published; make an event publish less often | **major** |

A mod that uses a hook declares the minor version that introduced it (`"imlight": "^1.2.0"`). On a server patched to
an older integration API the mod is rejected with `dependency.version_mismatch` at startup instead of failing at run
time. The patched Director bumps `ImlightModding.IntegrationApiVersion` in the same patch series that changes the
surface (see [integration/imlight/README.md](../integration/imlight/README.md)).

### Binary and source compatibility

* **Binary**: a mod compiled against SpiralLoader API `X` keeps loading on any host that provides the same
  `SpiralLoader.Api` assembly version without being rebuilt. The prebuilt `api-tour` fixture proves this for
  `0.1.0.0`.
* **Source**: a mod keeps compiling against newer *minor* versions. Nullability annotations are part of the surface,
  so tightening one is recorded as a change even though it only produces a warning.
* Imlight's own assemblies are **not** versioned by SpiralLoader; the supported Imlight revisions are listed in the
  README. A mod that depends on Imlight internals should pin `compatibility.imlightCommits`.

## Baselines

`api-baseline/<release set>/` holds the recorded public surface of a release, one sorted line per type and member,
produced by the same reflection code everywhere (`tools/SpiralLoader.ApiSurface`). `1.2.0` is the surface that
shipped as SpiralLoader API `0.1.0` with Imlight integration API `1.2.0`.

| File | Contents | Checked by |
| --- | --- | --- |
| `SpiralLoader.Api.txt` | public surface of `SpiralLoader.Api.dll` | `ApiCompatibilityTests` (exact match with the newest baseline; nothing removed or changed relative to *every* baseline) |
| `Imlight.CoreLib.Modding.txt` | public surface of the real patched `Imlight.CoreLib.Modding` sources, as compiled by the Imlight verify harness. The member count (`VERIFY: imlight-api members=N`) is asserted from `integration/imlight/verify/expected-output.txt` | `verify-patch.sh` / `Verify-Patch.ps1`, on both supported Imlight revisions. On a machine with a full patched Imlight build the same file is meant to be checked against the real `Imlight.CoreLib.dll` (not yet run: see the Windows handoff): `ApiSurface check Imlight.CoreLib.dll api-baseline/1.2.0/Imlight.CoreLib.Modding.txt --namespace Imlight.CoreLib.Modding` |
| `DiagnosticCodes.txt` | every diagnostic code released with that set | `ApiCompatibilityTests` (no code may disappear or change; additions must be documented and have a hint) |

What a failing check means:

* **Something removed or changed** — potentially breaking. Decide whether it is justified; if so bump the version as
  the tables above require, list it in the changelog below, and record a new baseline.
* **Only additions** — compatible. Bump the minor version, record a new baseline, document the addition.

### Checking and updating a baseline

```bash
dotnet build src/SpiralLoader.Api -c Release
dotnet build tools/SpiralLoader.ApiSurface -c Release
tool=tools/SpiralLoader.ApiSurface/bin/Release/net10.0/SpiralLoader.ApiSurface.dll

# Compare a build with the recorded baseline (exit 1: removed/changed members; --strict also fails on additions).
dotnet $tool check src/SpiralLoader.Api/bin/Release/net10.0/SpiralLoader.Api.dll api-baseline/1.2.0/SpiralLoader.Api.txt --strict

# Record the surface of a new release set (the directory name is the integration API version of that release).
mkdir api-baseline/1.3.0
dotnet $tool dump src/SpiralLoader.Api/bin/Release/net10.0/SpiralLoader.Api.dll --label "SpiralLoader.Api <version>" > api-baseline/1.3.0/SpiralLoader.Api.txt
dotnet $tool dump src/SpiralLoader.Runtime/bin/Release/net10.0/SpiralLoader.Runtime.dll --type SpiralLoader.Runtime.DiagnosticCodes > api-baseline/1.3.0/DiagnosticCodes.txt
```

The Imlight surface comes from the harness because it can only be compiled there: when the verify script reports
`FAIL` lines from the baseline, it prints the Director output; copy the block from `surface Imlight.CoreLib.Modding …`
to the last member line into `api-baseline/<new>/Imlight.CoreLib.Modding.txt` and update the `members=N` line in `expected-output.txt`. Directory names are plain
`major.minor.patch`; the newest one is the exact-match target, older ones remain "must not break" targets. The same
tool works on any assembly, so a mod author can baseline their own public API (`--namespace MyMod.Api`).

The `ApiTour` fixture (see [compatibility-testing.md](compatibility-testing.md#the-prebuilt-fixture)) is the binary
counterpart: a mod compiled once against `0.1.0.0` that exercises the API areas and keeps being loaded by the
current runtime.

## Deprecation

Before removing something mods may use, mark it `[Obsolete("…use X instead; removed in <version>")]` for at least one
minor release and say so in the changelog, unless it is a security or data-safety problem. While the API is `0.x`
this is best effort, not a promise.

## Changelog of contract changes

| Release set | Change |
| --- | --- |
| `1.2.0` (baseline) | `SpiralLoader.Api` `0.1.0` and Imlight integration API `1.2.0` recorded as the first baseline. No public API change |
| after `1.2.0` | Additive, `SpiralLoader.Runtime` only: diagnostic codes `assembly.api_newer` and `assembly.api_older`; `ModDiagnostic.Hint`, `DiagnosticHints`, `ModHostOptions.ModFolders`; `SpiralLoader.Host --check`, `--mod`, `--imlight-api`, `--warnings-as-errors`. A mod built against a newer `SpiralLoader.Api` assembly version than the host provides is now refused with `assembly.api_newer` instead of failing later (no mod buildable before could hit it) |
