# Audit: Imlight and Spiral Shuffle (milestone 1)

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
  NuGet reports Akka.Remote 1.5.0 as having a critical advisory (GHSA-jhpv-4q4f-43g5, NU1904). Upstream issue,
  not changed by SpiralLoader.
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
| Message services | Hard-coded `HashSet<Type>` in `GameServiceFactory` / `LoginServiceFactory` | `MessageService` is **internal** | No (needs a factory hook and access to an internal base) |
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

Only `Program.cs` (thread-pool warm-up, `LoginWarmup`, explicit `StartListening`) and `Imlight.ini` (one new
key) differ among the files the integration touches. The patch series applies to both without `--3way`, and
the verification harness passes on both.

## 2. Spiral Shuffle

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

"Implemented" means available after this milestone's Imlight patches. "Planned" items are specified in
[next-milestone.md](next-milestone.md).

| Spiral Shuffle change | Purpose | SpiralLoader mechanism | Status |
| --- | --- | --- | --- |
| `GameServiceFactory`: add `EnemyInspectorService` | new per-session service | `ImlightRegistries.GameMessageServices` | Implemented (needs access to internal `MessageService`, below) |
| `CharacterService`: new `MSG_MESSAGE_PROCESS` handler for the menu choice | handle client Lua reply on login | `ImlightRegistries.LoginMessageServices` (a mod service receives the fanned-out packet) | Implemented (same caveat) |
| `LootGranter`: `RandomizeLoot` before granting | change drops and gold | `LootGrantingEvent` | Implemented |
| Copy `RandomizerLevelRequirement` (`ReqMagicLevel` handler) | requirement Imlight lacks | Existing AppDomain scan of `BaseRequirementHandler<T>` (mod assemblies load before the scan) | Works in principle; uses internal `NumericRequirement`; unverified without a full build |
| `MinigameService`: ignore the inspector's job id | avoid fan-out collision | Client-process job id registry | Planned |
| `CharacterService`: after `MSG_CHARACTERLIST`, start `Randomizer/Menu{mode}.lua` | open mod UI at character select | `CharacterListSentEvent` with a send-to-client helper | Planned |
| `GameTransitionService`: pick realm by mode | route players to a realm | Cancellable `GameServerSelectingEvent` | Planned |
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
Imlight's. A separately compiled mod cannot see internal members. The full list a port needs is part of the
next milestone. Options, in order of preference:

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
  `ImlightModding.IntegrationApiVersion` (currently `1.0.0`): the version of the hook surface, not of Imlight.
- Wizard101 client data must match the server's `GameRevision`. Spiral Shuffle requires
  `r806919.Wizard_1_610`; mods declare this in `compatibility.clientRevisions`.
- .NET 10 SDK everywhere (Imcodec's `global.json` asks for `10.0.0`, `rollForward: latestMinor`).
