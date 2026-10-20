# Imlight integration and modding compatibility research

Audited 2026-10-19. Every statement here was checked against the source trees named below; line references
drift, so search for the quoted identifiers.

| Repository | Revision audited |
| --- | --- |
| [Revive101/Imlight](https://github.com/Revive101/Imlight) | `b07987168fcfa2d123a5d78df90ca0c76405e427` (Spiral Shuffle's pin, 2026-10-09) and `2a16010e59b95cc449d8e21542fbf38440e06bcf` (`quality-assurance` HEAD, 2026-10-16) |
| [Jooty/Imcodec](https://github.com/Jooty/Imcodec) (Imlight submodule) | `caef2683` (pin at b0798716), `d6ea4982` (pin at HEAD); tag `r806919.Wizard_1_610` = `b82609a9` |
| [ljste/spiral-shuffle](https://github.com/ljste/spiral-shuffle) | `4822893` ("Release Spiral Shuffle randomizer") |

## 1. Imlight

### Build and runtime shape

- .NET 10 (`net10.0`), three projects: `Imlight.Common` (config + Serilog logger), `Imlight.CoreLib` (all
  server logic), `Imlight.Director` (the executable). No tests in the repository.
- `Imlight.CoreLib` references seven Imcodec projects. `Imcodec.ObjectProperty` and `Imcodec.MessageLayer` use
  **Roslyn source generators fed by a WizWalker type dump** (`GeneratorInput/*.json`). Only a `!base`
  placeholder is committed (an older dump was deliberately deleted, Imcodec commit `bad8594`).
  **Consequence: Imlight.CoreLib and Imlight.Director cannot be compiled without a client-derived type dump.**
  The generated types include everything wire- or template-shaped: `CoreTemplate`, `Requirement`,
  `ResultList`, `GAME_5_PROTOCOL`, `MG2_MESSAGES_43_PROTOCOL`, and so on.
- `Imlight.Common` has no Imcodec dependency and builds from a clean clone.
- Package drift: `Imlight.Common` uses Akka 1.5.12 / Akka.Remote **1.5.0**; CoreLib and Director use 1.5.33.
  Both Akka.Remote versions are below 1.5.52 and are affected by the critical upstream advisory
  [GHSA-jhpv-4q4f-43g5 / CVE-2025-61778](https://github.com/advisories/GHSA-jhpv-4q4f-43g5): TLS-enabled
  Akka.Remote did not require the outbound peer to present its certificate, so certificate-based authentication
  was not mutual. Akka.NET lists 1.5.52 as patched. The advisory says this specific issue does not affect an
  application that does not use TLS or runs only on a private network it fully controls; operators must assess
  their own configuration. This is an upstream dependency issue, not fixed by SpiralLoader. The full patched
  builds reported NuGet audit warnings; they were not hidden or treated as build errors.
- Configuration: `Config/Imlight.ini` via `ConfigurationManager` (sections, `GetSection`, `GetSetting("Section.Key")`).
  The shipped `GameRevision` is `r803238.Wizard_1_610`; Spiral Shuffle overrides it to `r806919.Wizard_1_610`.
- Logging: static `Imlight.Common.Logger` (`Information(string, object[]?)`, `{0}`-style templates, Serilog
  console + file + Seq sinks).

### Startup sequence (`Imlight.Director/Program.cs`)

`ConfigurationManager.Initialize` → `AkkaConfiguration.CreateActorSystem` → patch server (awaited) →
`ResourceContainer` + `SpiralDB.Load()` → login server → game servers (`MSG_CREATEGAMESERVER`, one per
realm) → player database + admin account → (HEAD only: `LoginWarmup.Run()`, `MSG_STARTLISTENING`) →
"Imlight may now be connected to." → idle loop. There is no shutdown path; the process is killed.

### Execution model

- Akka.NET actors throughout. Each client connection is a `SessionActor`; it asks its `ServiceFactory`
  (`GameServiceFactory` / `LoginServiceFactory`) for a `HashSet<Type>` of `MessageService` types and creates
  one child actor per type (`Props.Create(type, sessionActor)`).
- `SessionActor.HandlePacket` **fans out**: every service that declares a `[MessageHandler]` for a packet type
  receives it. This is why Spiral Shuffle had to make `MinigameService` ignore its job id.
- `MessageHandlerTable` discovers `[MessageHandler]` methods by reflection per receiver type.

### Existing extension mechanisms (all reflection-based, none designed for third parties)

| Mechanism | Discovery | Base type visibility | Usable from a separately compiled assembly? |
| --- | --- | --- | --- |
| Message services | Hard-coded `HashSet<Type>` in `GameServiceFactory` / `LoginServiceFactory` | `MessageService` is **internal** | No (needs a factory hook and access to an internal base). **Patches 0003 + 0011/0012** add the factory hook and the public `ModMessageService` bridge |
| Requirement handlers | `RequirementDispatcher` static ctor scans `AppDomain.CurrentDomain.GetAssemblies()` | `BaseRequirementHandler<T>` public | Yes, if the assembly is loaded before first use; first match wins, order unspecified |
| Result handlers | `ResultDispatcher` static ctor, same scan | `BaseResultHandler<T>` public | Same as above |
| Zone entity components | `ZoneEntityComponentRegistry` static ctor, same scan | `ZoneEntityComponent` public, `IComponentFactory` **internal** | No (the marker interface is internal) |
| Chat commands | `CommandDispatcher` scans `Assembly.GetExecutingAssembly()` only | `CommandProtocol` **internal** | No |
| Resources | `ResourceContainer` scans CoreLib only | public bases | No |

Hazards found:

1. The three AppDomain scans call `a.GetTypes()` unguarded inside static constructors. One loaded assembly with
   an unresolvable reference throws `ReflectionTypeLoadException`, which becomes a `TypeInitializationException`
   for the whole registry. **Fixed by patch 0006.**
2. Duplicate handlers for one requirement/result type are resolved by dictionary iteration order, so a mod
   cannot reliably override a built-in handler. Not fixed yet (see next milestone).

### Differences between the pin and HEAD that matter to SpiralLoader

Among the files the integration touches, upstream HEAD changed `Program.cs` (thread-pool warm-up, `LoginWarmup`,
explicit `StartListening`), `Imlight.ini` (one new key), `CharacterService.cs` and `GameTransitionService.cs`
(milestone 2 hooks). The patch series applies to both with `git am --3way` (which the setup scripts use) and the
verification harness passes on both. Behaviour differences the hooks tolerate:

- `CharacterService` at HEAD no longer has the "unknown account" error path (`GetSocketAccount` throws) and skips a
  character it cannot serialize instead of aborting the list, so `CharacterListSentEvent` fires after a partial
  list there; at the pin it does not fire when serialization of any character fails.
- `GameTransitionService.GetGameServer` at HEAD returns null on timeout and the handler answers with
  `NoZonesAvailable`. The `GameServerSelectingEvent` patch consists of pure insertions around that code, so it
  does not conflict with the upstream rewrite and a mod's chosen realm bypasses `GetGameServer` once.
- `SendErrorToSocket` at HEAD no longer closes the session; a veto or unknown realm still sends
  `MSG_CHARACTERSELECTED { Error = 1 }`. At the pin it calls `CloseSession()`, so the same veto closes the session
  there (correction to the milestone 2 wording, which said the session always closes; the harness now asserts
  both).
- `SessionActor`'s supervisor (`AllForOneStrategy`) answers an unknown service exception with `Stop` at the pin
  (every service of the session stops) and `Resume` at HEAD. The mod service host catches mod exceptions itself,
  so mod faults behave the same on both.
- `SessionActor.Dispose` (both revisions) waits for every service's `MSG_PREDISPOSE` reply, then tells
  `MSG_DISPOSE` and stops itself, which stops its children; a child can be stopped before it processes
  `MSG_DISPOSE`. Observed in the harness; `ModMessageService.OnSessionEnded` therefore runs at pre-dispose.

## 2. Spiral Shuffle compatibility reference (not a project deliverable)

This section records an independent project as a technical reference for understanding extension needs. It does not define a planned port, acceptance criterion, or commitment by its maintainer. Any future third-party integration would need to be undertaken by its maintainer or another interested contributor, with appropriate licensing and attribution.

### What it is

A Wizard101 randomizer (spells, gear stats, drops, creatures; Shuffle/Chaos/Nightmare modes, optionally
creature shuffle) delivered as a Windows setup that builds a private server and a separate client profile.

### How it integrates today

`setup.py` downloads Imlight at `b0798716` (archive SHA-256 pinned), copies binaries from an existing
Revive101 install, rewrites `Imlight.ini` (6 realms named after modes, private ports, private save directory),
then `prepare_source.py`:

- copies `Imlight.CoreLib` source, rewrites every `—` (em dash) to `,` in every `.cs` file;
- applies **27 exact-match string replacements in 12 files** (`replace()` asserts exactly one occurrence);
- copies **6 files** into the tree, one of which overwrites an existing Imlight file
  (`WorldTeleportDoorComponent.cs`, to list all worlds);
- builds a replacement `Imlight.CoreLib.dll` (`server-build/Imlight.CoreLib.csproj`, compiling the patched
  sources against DLLs from the user's install, which contain the generated Imcodec types) and drops it over
  the installed one.

Client side: `asset-tool` (C#, Imcodec) generates randomized `ObjectData/` and `Spells/` templates and a
`catalog.json`; Python scripts build `GUI/RandomizerMenu.gui` / `GUI/RandomizerEnemyInspector.gui` from the
client's own `CharacterSelectWindow.gui`, specialize `Menu.lua` per mode, rebuild `Root.wad` with the overrides
(`pack_assets.py`, guarded by an allow-list of path prefixes), rewrite the `Root.wad` record in
`LatestFileList.xml/.bin`, serve them from `host.py` (a local HTTP patch server), and copy a 25 GB client.

Server ↔ client communication uses the client's Lua process API: the server sends
`MSG_START_CLIENT_PROCESS { JobID, ScriptName }`, the script replies with `SendProcessMessage` carrying an
`MSG_MG2_REWARDS` payload, which arrives as `MSG_MESSAGE_PROCESS { JobID, Message }`. Job ids 770101 (menu)
and 770102 (enemy inspector).

### Patch inventory and how each maps onto SpiralLoader

Milestone 2 (integration API 1.1.0) read these call sites in the pinned source: `CharacterService.ReceiveRequestCharacterList`
(the final `MSG_CHARACTERLIST` is the last statement; the error and serialization-failure paths return earlier),
`GameTransitionService.ReceiveSelectCharacter` (`GetGameServer()` asks the login server for `MSG_GETBESTSERVER`;
`MSG_QUERYREALMSERVER` is already served by `GameServerPool` and answers `RealmName = null` for an unknown
realm), and `MinigameService.ReceiveMessageProcess` (never looks at `JobID` before decoding, and would
dereference a null `_minigameJob` for a player who is not in a minigame). `ProcessSupervisor.GenerateProcessId`
draws random ids in `[1, int.MaxValue)`, which can collide with a mod's claimed id, so it skips claimed ids.

"Implemented" means available after this milestone's Imlight patches. "Planned" items are specified in
[next-milestone.md](next-milestone.md).

| Spiral Shuffle change | Purpose | SpiralLoader mechanism | Status |
| --- | --- | --- | --- |
| `GameServiceFactory`: add `EnemyInspectorService` | new per-session service | `ModMessageService` in `ImlightRegistries.GameMessageServices` | Implemented (API 1.2.0; harness-verified, see integration README) |
| `CharacterService`: new `MSG_MESSAGE_PROCESS` handler for the menu choice | handle client Lua reply on login | `ModMessageService` in `ImlightRegistries.LoginMessageServices`; replies for the mod's claimed job id are routed only to that mod | Implemented (API 1.2.0; harness-verified with a synthetic reply, no real client) |
| `LootGranter`: `RandomizeLoot` before granting | change drops and gold | `LootGrantingEvent` | Implemented |
| Copy `RandomizerLevelRequirement` (`ReqMagicLevel` handler) | requirement Imlight lacks | Existing AppDomain scan of `BaseRequirementHandler<T>` (mod assemblies load before the scan) | Works in principle; uses internal `NumericRequirement`; unverified without a full build |
| `MinigameService`: ignore the inspector's job id | avoid fan-out collision | `ImlightRegistries.ClientProcessJobs` (+ `ProcessSupervisor` never allocates claimed ids) | Implemented (API 1.1.0; compile-unverified call sites, see integration README) |
| `CharacterService`: after `MSG_CHARACTERLIST`, start `Randomizer/Menu{mode}.lua` | open mod UI at character select | `CharacterListSentEvent` with `Send(IMessage)` | Implemented (API 1.1.0) |
| `GameTransitionService`: pick realm by mode | route players to a realm | Cancellable `GameServerSelectingEvent` (`RealmName` -> `MSG_QUERYREALMSERVER`, the lookup Shuffle uses) | Implemented (API 1.1.0) |
| `ZoneEntity` (2): swap creature template and client template id | creature shuffle | `ZoneEntityTemplateResolvingEvent` | Planned |
| `NpcComponent` (3): roll and apply creature stats | creature stat randomization | `NpcStatsInitializingEvent` | Planned |
| `CombatDuelComponent`: handle `EnemyInspectorQuery` | inspector data from the duel | Zone component contributions | Planned |
| `CombatDeck` (4) + `CombatDuelSubCircle` (2): substitute spell ids | spell randomization | `CombatSpellResolvingEvent` | Planned |
| `WorldTeleportDoorComponent` overwrite | list every world | `WorldTeleportListBuildingEvent` | Planned |
| `QuestService` (3): skip client messages for hidden quests | hidden helper quests | Upstream bug-fix candidate (not mod-specific) | Propose upstream |
| `TutorialService` (6): robust tutorial intro hand-off | tutorial completion | Upstream bug-fix candidate | Propose upstream |
| Em dash rewrite | works around their build pipeline | Not needed: SpiralLoader patches are byte-exact git patches | n/a |
| `Imlight.ini` rewrite (realms, ports, saves) | isolated server profile | Profile configuration overlays | Planned |
| `Root.wad` rebuild, `LatestFileList` patch, `host.py` | client asset delivery | `assets` declarations + `AssetOverlayPlan`; WAD writer + patch-list writer | Planning implemented; writing planned |
| `asset-tool` catalog generation from the user's client | seeded data generation | Tool entrypoint kind / generation stage | Planned |

### Access to Imlight internals

Spiral Shuffle is compiled *into* `Imlight.CoreLib`, so it freely uses `internal` members. Verified examples:
`MessageService`, `NumericRequirement`, `IComponentFactory`, and its own `internal` classes calling into
Imlight's. A separately compiled mod cannot see internal members. The milestone 2 local run confirmed this for
`MessageService` (a mod deriving from it does not compile against the real `Imlight.CoreLib`); the cloud harness
had hidden it with a public stub. **Resolved for message services by option 1:** patches 0011/0012 add the public
`ModMessageService` bridge, and the harness now compiles the real, internal `MessageService`. The rest of the list
a third-party integration may need (`NumericRequirement`, `IComponentFactory`, …) is an independent extension concern. Options, in order of
preference:

1. Make the specific members a mod needs public in Imlight (upstream change), or expose them through
   SpiralLoader-owned public wrappers added by the patch series.
2. Compile the mod against a publicized reference assembly and mark it with
   `IgnoresAccessChecksToAttribute("Imlight.CoreLib")` (the approach used by Unity/BepInEx modding). The .NET
   runtime honours this attribute, but SpiralLoader has **not yet verified** it against Imlight.

Mods that touch internals should pin `compatibility.imlightCommits`.

## 3. Version compatibility constraints

- Imlight has no semantic version; identity is the git commit. The patched Director stamps it into
  assembly metadata (`ImlightCommit`) at build time.
- SpiralLoader exposes `imlight` as a built-in dependency whose version is
  `ImlightModding.IntegrationApiVersion` (currently `1.2.0`): the version of the hook surface, not of Imlight.
- Wizard101 client data must match the server's `GameRevision`. Spiral Shuffle requires
  `r806919.Wizard_1_610`; mods declare this in `compatibility.clientRevisions`.
- .NET 10 SDK everywhere (Imcodec's `global.json` asks for `10.0.0`, `rollForward: latestMinor`).
