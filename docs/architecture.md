# SpiralLoader architecture

SpiralLoader plays the role Fabric plays for Minecraft: a small loader, a stable public API, and a maintained
set of hook points in the game server. Mods are separately compiled .NET assemblies plus data, described by a
manifest, loaded once at server start.

```
            ┌──────────────────────── Imlight.Director (patched) ─────────────────────────┐
            │ Program.Main: config → StartModLoader() → actors → resources → servers       │
            │                    │                          ▲ lifecycle events            │
            └────────────────────┼──────────────────────────┼─────────────────────────────┘
                                 ▼                          │
  SpiralLoader.Imlight ── ImlightModLoader.Start(settings, identity, log writer, registries)
                                 │
  SpiralLoader.Runtime ── ModHost: discover → parse → filter → validate → resolve → load → init → freeze
                                 │                              │
                                 │            ModLoadContext (one AssemblyLoadContext per mod)
                                 ▼
  SpiralLoader.Api ────── ISpiralMod, IModContext, IEventBus, IRegistry<T>, ModManifest, SemVer
                                 ▲
  Imlight.CoreLib (patched) ── ImlightModding.Events / Registries ── call sites publish events, read registries
```

## Projects

| Project | Depends on | Role |
| --- | --- | --- |
| `SpiralLoader.Api` | nothing | The only assembly mods must reference. Contracts, manifest model, versioning. Always provided by the host (`Private=false`). |
| `SpiralLoader.Runtime` | Api | The plugin manager. Host-agnostic. |
| `SpiralLoader.Imlight` | Runtime | Imlight bootstrap. **No Imlight references**: Imlight passes its INI section, identity and a log delegate in, so this is fully testable without Imlight. |
| `SpiralLoader.Host` | Runtime | Headless host for development and CI; boots mods the same way the Imlight bootstrap does; `--check` is the developer compatibility check. |
| `tools/SpiralLoader.ApiSurface` | nothing | Prints and compares public API surfaces; its describer is also linked into the tests and the Imlight verify harness. Baselines live in `api-baseline/`. |
| `integration/imlight/patches` | — | The patch series that adds hook points to Imlight (applied with `git am`). |
| `integration/imlight/verify` | Imlight.Common, SpiralLoader.Imlight | Compiles the real patched Director against stubs and boots it. |

## Load pipeline (`ModHost.Load`)

1. **Discover.** Each immediate subdirectory of each mods directory that contains `spiralmod.json` is one
   mod. Directories are visited in ordinal order; names starting with `.` are skipped.
2. **Parse.** `ManifestParser` reports every problem at once. Errors reject the mod; unknown fields warn.
3. **Filter.** Disabled ids (configuration) become `Disabled`. Ids equal to a built-in are rejected.
   Duplicate ids reject every copy.
4. **Validate files and compatibility.** Declared assemblies and asset directories must exist.
   `compatibility.imlightCommits` / `clientRevisions` are checked against the host (`Strict` rejects,
   `WarnOnly` warns, unknown host value warns).
5. **Resolve** (`DependencyResolver`). Repeats until stable: a mod is rejected if a `depends` entry is missing,
   rejected, or out of range, or if a `breaks` entry is present and in range. Built-ins (`spiralloader`,
   `dotnet`, `imlight`) satisfy dependencies like mods. Then a topological sort: hard edges from `depends`,
   soft edges from present `recommends`, `loadAfter`, `loadBefore`. Ties go to the lowest id. Hard cycles
   reject their members (and dependents); soft cycles are broken deterministically with a warning.
6. **Load and initialize**, one mod at a time in order:
   - a required dependency that failed at this stage fails the mod (`dependency.failed`);
   - each declared assembly's metadata is read first: a reference to a `SpiralLoader.Api` assembly version newer than
     the host's fails the mod before any of its code runs (`assembly.api_newer`), an older one warns
     (`assembly.api_older`). The API assembly version changes only for breaking API changes;
   - declared assemblies load into the mod's `ModLoadContext`, and `GetTypes()` is forced so an unresolvable
     reference fails the mod now, with a clear message;
   - entrypoints for the host's kind (`server`) are constructed (public parameterless constructor) and
     `Initialize(IModContext)` is called;
   - if anything throws, the mod is `Failed` and **every subscription and registration it made is rolled
     back**. Its assemblies stay loaded (non-collectible).
7. **Freeze.** The event bus and all registries stop accepting registrations.
8. **Report and policy.** The full report is logged. Under `AbortStartup` (default) any rejected or failed mod
   shuts the initialized ones down and throws `ModLoadException`; under `ContinueWithoutFailedMods` the host
   continues. Then `ModsLoadedEvent` is published.

`Shutdown()` calls `ISpiralMod.Shutdown` in reverse load order. There is no unload or reload: changing mods
requires a restart.

## Assembly loading policy (`ModLoadContext`)

One non-collectible `AssemblyLoadContext` per mod. When a mod's code references an assembly:

1. **Host first.** If the host can provide it (it is `SpiralLoader.Api`/`Runtime`, on the host's trusted
   platform assembly list — Imlight, Akka, Imcodec, the framework — or already loaded in the default
   context), the default context's copy is used. A mod that ships its own copy gets a
   `assembly.shared_copy` warning and the host copy anyway. This keeps `Type` identity consistent: an
   `ISpiralMod` or `Wizard` in a mod is the host's type.
2. **Dependencies next.** Assemblies declared by the mod's `depends`/`recommends` (transitively) come from
   those mods' contexts, so mods share types with the mods they depend on (proven by the `HelloSpiral.Addon`
   example and `LoadingTests.DependentModSharesTypesWithItsDependency`).
3. **Private last.** Anything else resolves through the mod's `.deps.json` (`AssemblyDependencyResolver`) or
   its directory. Two mods may carry different private versions of the same library.

Assemblies are never unloaded: Akka actors, static caches, and Imlight's reflection registries hold
references, so collectible contexts would not actually unload and would add risk.

## Events

`IEventBus` is typed, synchronous, dispatch-by-exact-type.

- **Ordering:** priority (high first) → owner load order (host first) → registration order. Fully
  deterministic.
- **Mutation:** events are plain objects; handlers change their properties (filter-style hooks, e.g.
  `LootGrantingEvent.Results`). `CancellableEvent` stops lower-priority handlers and tells the host to skip
  its default action; `EventPriority.Monitor` handlers still see cancelled events.
- **Fault isolation:** a throwing handler is logged against its owner and counted; dispatch continues
  (`HandlerFaultPolicy.Isolate`). `Rethrow` exists for tests and development.
- **Threading:** after freeze the handler arrays are immutable, so `Publish` is lock-free and safe from any
  number of actors. Handlers run **on the publishing thread** — in Imlight, inside an actor's message
  processing. They must not block, must not keep references to actor-owned state, and should use
  `IActorRef.Tell` or a task for long work. This respects Akka's single-threaded-actor guarantee: a handler
  is just more code in the same actor turn.
- **Cost when unused:** call sites check `HasSubscribers<T>()` before allocating an event. With no loader
  attached Imlight uses `NullEventBus`, so unmodded behaviour and performance are unchanged.

Host-agnostic lifecycle events: `ModsLoadedEvent`, `ServerStartingEvent`, `ServerResourcesLoadedEvent`,
`ServerStartedEvent`, `ServerStoppingEvent`. Imlight-specific events live in `Imlight.CoreLib.Modding`.

## Registries

`IRegistry<T>` collects contributions the host consumes. Keys are `namespace:path`; entry ids are namespaced by
the contributing mod (`modid:name`). The host declares registries before `Load()` and may attach a validator
(for example "must derive from `ModMessageService` with a public parameterless constructor"); a rejected value throws in
the mod's `Initialize`, which fails and rolls back that mod. Entries are ordered by mod load order, then
registration order. Registries freeze with the bus. A host may declare a registry with `uniqueValues: true`
for claims (ids the host reserves): a value can then be registered once across all mods, and a duplicate
throws in the second mod's `Initialize` (reported as `init.failed`, naming the entry that holds the value).

Declared today:

| Key | Value | Consumed by |
| --- | --- | --- |
| `spiralloader:commands` | `ModCommand` | Headless host `--run`; Imlight `CommandDispatcher` fallback |
| `imlight:game_message_services` | `Type` (`ModMessageService`) | `GameServiceFactory` → per-session `GameModMessageServiceHost` |
| `imlight:login_message_services` | `Type` (`ModMessageService`) | `LoginServiceFactory` → per-session `LoginModMessageServiceHost` |
| `imlight:client_process_jobs` | `uint` (unique, non-zero) | `MinigameService`, `ProcessSupervisor` via `ImlightModding.IsClientProcessJobClaimed`; the mod service hosts route replies to the claiming mod |

### Mod message services (Imlight)

Imlight's `MessageService` is `internal`, so a mod cannot derive from it. The patch series adds a public
`ModMessageService` (a plain object, not an actor) and an internal `ModMessageServiceHost : MessageService`. When
a factory's registry holds at least one `ModMessageService` type, the factory adds the host type once; each
session then creates one host as an ordinary child of its `SessionActor`. The host publishes the union of the mod
services' `[MessageHandler]` message types as its `MessageHandlers` (the only change to Imlight's dispatcher is a
`protected` setter for that property), so `SessionActor`'s own dispatch table forwards those packets to it. The
host creates the mod services, attaches them to the session, and calls their handlers through Imlight's own
`MessageHandlerTable` on its actor thread. Client process messages (`MSG_MESSAGE_PROCESS`,
`MSG_CLIENT_PROCESS_TERMINATED`) are delivered only to the services of the mod that claimed the job id. Mod
constructor and handler exceptions are caught and logged against the owning mod, so they never reach the
session's supervisor (whose strategy differs between Imlight revisions). `OnSessionEnded` runs in the host's
pre-dispose step, the last point at which `SessionActor` reliably waits for its services.

## Manifest (`spiralmod.json`, schema 1)

```jsonc
{
  "schemaVersion": 1,                       // required
  "id": "spiral-shuffle",                   // required: ^[a-z][a-z0-9_-]{1,63}$
  "version": "1.0.0",                       // required: SemVer 2.0
  "name": "Spiral Shuffle", "description": "…", "authors": ["…"], "license": "AGPL-3.0-only",
  "entrypoints": { "server": ["SpiralShuffle.ShuffleMod"] },   // kind -> type name(s)
  "assemblies": ["SpiralShuffle.dll"],      // relative paths, load order
  "depends":    { "spiralloader": ">=0.1.0 <0.2.0", "imlight": "^1.0.0" },
  "recommends": { "enemy-inspector": "*" },
  "breaks":     { "other-randomizer": "<2.0.0" },
  "loadAfter": [], "loadBefore": [],
  "compatibility": {
    "imlightCommits": ["b07987168fcfa2d123a5d78df90ca0c76405e427"],
    "clientRevisions": ["r806919.Wizard_1_610"]
  },
  "assets": [
    { "kind": "wad", "source": "assets/root", "target": "GameData/Root.wad" },
    { "kind": "spiraldb", "source": "spiraldb", "target": "" }
  ],
  "custom": {}                              // free-form, ignored by the loader
}
```

Version ranges: `*`, `1.2.3`, `=`, `>`, `>=`, `<`, `<=`, `^`, `~`, `1.2.x`, space = AND, `||` = OR. Upper bounds
created by `^`, `~` and wildcards exclude prereleases of the bound.

## Diagnostics

Every problem is a `ModDiagnostic(Severity, Code, Message)` on the mod's record. Codes are stable (and recorded in
`api-baseline/`); messages name the mod's specifics (versions, folders, the real types in an assembly, the root cause
and how much was rolled back). `ModDiagnostic.Hint` adds what to do about the code (`DiagnosticHints`), and the report
prints it as a `fix:` line under each warning or error, after a `Needs attention:` line that names every failed mod and
its codes. Codes:

| Code | Meaning |
| --- | --- |
| `manifest.unreadable`, `manifest.invalid_json`, `manifest.invalid_field`, `manifest.unsupported_schema` | Manifest problems (reject) |
| `manifest.unknown_field` | Ignored field (warning) |
| `mod.disabled` | Disabled by configuration (info) |
| `mod.duplicate_id`, `mod.reserved_id` | Identity problems (reject) |
| `dependency.missing`, `dependency.version_mismatch`, `dependency.rejected`, `dependency.cycle` | Resolution (reject) |
| `dependency.failed` | A required dependency failed during loading (fail) |
| `breaks.present` | Incompatible mod installed (reject) |
| `recommends.missing`, `recommends.version_mismatch`, `ordering.soft_cycle` | Warnings |
| `compat.imlight_commit`, `compat.client_revision` | Pin mismatch (reject in Strict, warn otherwise) |
| `assembly.missing` | Declared file missing (reject) |
| `assembly.load_failed`, `entrypoint.not_found`, `entrypoint.invalid`, `init.failed` | Loading (fail) |
| `assembly.shared_copy` | Mod shipped a host-provided assembly (warning) |
| `assembly.api_newer` | The assembly was built against a newer `SpiralLoader.Api` (by assembly version) than the host provides (fail, before the mod's code runs) |
| `assembly.api_older` | The assembly was built against an older `SpiralLoader.Api` than the host provides; it may need a rebuild (warning) |
| `asset.invalid_path`, `asset.missing` | Asset declarations (reject) |
| `asset.conflict` | Two mods override the same asset entry without an ordering between them (reported by the asset plan) |
| `host.mods_directory_missing` | A configured mods directory does not exist (info, host-level, not tied to a mod) |

The headless host's `--check` mode turns this report into a developer verdict (see
[compatibility-testing.md](compatibility-testing.md)).

## Trust model

Native mods are **trusted code**. They run in the server process with full permissions, can call any public
API, can use reflection, and can crash or corrupt the server. SpiralLoader validates structure and isolates
*mistakes* (bad manifests, missing dependencies, exceptions in init or handlers); it is not a sandbox and does
not defend against malicious mods. Only install mods you trust.

## Data subsystems (designed for, not finished)

| Subsystem | Today | Plan |
| --- | --- | --- |
| Lua scripts, GUI layouts | Shipped as entries of `Root.wad` (`Scripts/…`, `GUI/…`), declared as `wad` assets | Overlay builder + client-process API (start script, route replies by job id) |
| WAD archives | `AssetOverlayPlanner` merges declarations deterministically and reports conflicts | Write overlay archives with `Imcodec.Wad`, rewrite `LatestFileList.xml/.bin`, serve through Imlight's patch server |
| SpiralDB | `spiraldb` asset declarations validated and planned | Overlay JSON into `SpiralDB.Load()` |
| Profiles | `Profile` scopes `DataDirectory` (`moddata/<profile>/<mod>`) | Per-profile enabled-mod sets, INI overlays, save isolation |
