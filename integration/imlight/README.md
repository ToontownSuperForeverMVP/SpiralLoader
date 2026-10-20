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
| 0007 | `feat: publish CharacterListSentEvent after the character list is sent` | `CharacterService.ReceiveRequestCharacterList`, `CharacterListSentEvent` |
| 0008 | `feat: let mods route or veto the game server selection` | `GameTransitionService` (`TrySelectModGameServer`), `GameServerSelectingEvent`. A veto reuses the service's error path, which closes the session at b0798716 but not at 2a16010e (documented on the event, asserted per revision) |
| 0009 | `feat: let mods claim client process job ids` | `ImlightRegistries.ClientProcessJobs`, `ImlightModding.IsClientProcessJobClaimed`, `MinigameService`, `ProcessSupervisor`, registry declaration in `Program.cs` |
| 0010 | `feat: raise the mod integration API to 1.1.0` | `ImlightModding.IntegrationApiVersion` |
| 0011 | `feat: let separately compiled mods provide message services` | New `Modding/ModMessageService.cs` (public `ModMessageService`, internal `ModMessageServiceHost` + game/login hosts); `ImlightModding.ValidateMessageService` / `WithModServices`; both service factories; `ReceiveProtocolDispatcher.MessageHandlers` gets a `protected` setter |
| 0012 | `feat: route claimed client process replies to the claiming mod` | Job id → owner table in `ImlightModding`; host routes `MSG_MESSAGE_PROCESS` / `MSG_CLIENT_PROCESS_TERMINATED` by owner; `ModMessageService.StartClientProcess` |
| 0013 | `feat: raise the mod integration API to 1.2.0` | `ImlightModding.IntegrationApiVersion` |

Dependency direction: `Imlight.CoreLib → SpiralLoader.Api` (no third-party dependencies) and
`Imlight.Director → SpiralLoader.Imlight → SpiralLoader.Runtime`. CoreLib never references the runtime.

Behaviour without mods: `ImlightModding.Events` is `NullEventBus` until the Director attaches the loader; with
no `mods/` folder (or `Enabled = false`) every call site is a single `HasSubscribers` check returning false and
every service factory returns its built-in set unchanged (no mod service host is added unless a
`ModMessageService` is registered). The harness's second run asserts this with the client-process mods disabled.

## Scripts

| Script | Purpose |
| --- | --- |
| `setup-imlight.sh` / `Setup-Imlight.ps1` | Clone Imlight into `external/Imlight` (or a given path) at the pinned commit (override with `IMLIGHT_COMMIT` / `-Commit`), init submodules, apply the series on branch `spiralloader` |
| `regen-patches.sh` | After committing on the `spiralloader` branch of a checkout: delete and regenerate `patches/` with `git format-patch` |
| `verify/verify-patch.sh` / `verify/Verify-Patch.ps1` | Apply the series to a fresh checkout and boot the real patched Director against stubs (see `docs/building.md`) |

## Verification status

"Compiled" means the real patched Imlight file is compiled by the harness against stubs of the Imcodec-generated
types (`verify/ImcodecStubs.cs`, which follow how the pinned source uses those types) and of the few Imlight
packet classes that need generated zone types (`ZONE_102_PROTOCOL`, `CHARACTER_103_PROTOCOL`, in
`verify/Stubs.cs`). "Executed" means the harness drives it (`verify/Scenarios.cs`) and the expected-output files
assert the result. Everything below holds for both b0798716 and 2a16010e, with both verify scripts.

| Part | How verified |
| --- | --- |
| Series applies to b0798716 and 2a16010e | `git am` in the verification scripts |
| `Program.cs`, `ImlightModding.cs`, `ImlightEvents.cs`, `AssemblyTypes.cs`, `GameServiceFactory.cs`, `LoginServiceFactory.cs`, `ServiceFactory.cs`, `ZoneEntityComponentRegistry.cs` | Compiled (real patched files) and executed by the harness |
| `SessionActor.cs`, `MessageService.cs`, `ReceiveProtocolDispatcher.cs`, `MessageHandlerTable.cs`, `Exceptions.cs`, `IHandshakeService.cs`, `ACCOUNT_104_PROTOCOL.cs`, `OnlinePlayer.cs` | Compiled (real) and executed: every scenario runs in a real `SessionActor` created from the real patched factories; client packets enter through `MSG_RECEIVEDPACKET` → `HandlePacket` → the session's dispatch table → the services; sessions close through the real dispose path. Only `SocketListener`/`SocketSender` are stubbed |
| `ModMessageService.cs` (0011, 0012) | Compiled; executed with three separately compiled mods that reference only `SpiralLoader.Api` and the public harness surface (the harness reports `MessageService.public=False`). Asserted: a mod claims 770101, starts the menu process from `CharacterListSentEvent` and its login `ModMessageService` receives the synthetic `MSG_MESSAGE_PROCESS` reply and answers the client; in a game session a claimed reply reaches only the owning mod's service and is not decoded by `MinigameService`, while rolled-back and unclaimed ids are decoded by `MinigameService` and reach no mod; `MSG_CLIENT_PROCESS_TERMINATED` follows the same routing; `StartClientProcess` refuses another mod's id; a throwing handler and a throwing constructor are logged against their mod and the session keeps working; `OnSessionEnded` runs for every session; a duplicate claim fails the second mod (`init.failed`) and rolls back its service and its other claim while the first mod's claim keeps working; with the mods disabled the factories and all replies are vanilla |
| `CharacterService.cs` (`CharacterListSentEvent`) | Compiled; executed: event fires after `MSG_CHARACTERLIST`, a mod `Send` arrives last on the socket |
| `GameTransitionService.cs` (`GameServerSelectingEvent`) | Compiled; executed for default, mod-chosen realm (`MSG_QUERYREALMSERVER`), veto and unknown realm; whether the error closes the session is asserted per revision (closes at b0798716, stays open at 2a16010e) |
| `MinigameService.cs` (claimed job ids) | Compiled; executed in a real game session (see `ModMessageService.cs`) |
| `ProcessSupervisor.cs` (skips claimed ids) | Compiled only (the id is random, so no scenario) |
| `SERVER_100_PROTOCOL.cs`, `SERVICE_101_PROTOCOL.cs`, `PROCESS_107_PROTOCOL.cs`, `ServerInterfaces.cs` | Compiled (real, unmodified) so that dispatch and the server messages are the genuine ones |
| Public `Imlight.CoreLib.Modding` surface (integration API 1.2.0) | The harness prints the surface of the real patched `ImlightModding.cs`, `ImlightEvents.cs` and `ModMessageService.cs` as compiled; both scripts assert it line by line (and the member count) against `api-baseline/1.2.0/Imlight.CoreLib.Modding.txt`, on both revisions. A removed or changed member fails; an addition fails until the baseline is deliberately updated (see `docs/api-compatibility.md`) |
| Incompatible integration API, then disabling the mod | `verify/DataMods/future-api-verify` (data-only, `"imlight": "^1.3.0"`): rejected with `dependency.version_mismatch` while the other mods load (Continue policy); in the vanilla run it is disabled and the Director starts under the Abort policy |
| Commit stamping targets in `Imlight.Director.csproj` | Exercised in isolation (git checkout, no git, explicit property) |
| `LootGranter.cs`, `CommandDispatcher.cs`, `RequirementDispatcher.cs`, `ResultDispatcher.cs` edits | Compiled by the full patched Release builds with real Imcodec generator inputs on both supported revisions |
| Generated-type assumptions in the 1.1.0/1.2.0 call sites | Verified by full builds and a probe against each generated `Imcodec.MessageLayer.dll`: job ids are `uint`; `MSG_MESSAGE_PROCESS.Message` and `MSG_START_CLIENT_PROCESS.ScriptName` are `Imcodec.IO.ByteString` with working string conversions; start/kill fields are settable; all four process messages implement `IMessage`; the `Wizard`/`Account` call sites compile |
| Whole `Imlight.CoreLib` / `Imlight.Director` build and isolated Director startup | Full patched Release builds passed on both revisions with real generator inputs. The pinned Director started with the separately compiled external mod and with SpiralLoader disabled; no Wizard101 client was launched |
| Real client session | Not run: no separate safe client setup was available, so no client Lua or live reply round-trip was executed |

## Remaining client validation

The full patched Release builds, external mod builds, generated-type probes, synthetic dispatch against the actual
built CoreLib assemblies, isolated Director startup, and vanilla startup with SpiralLoader disabled were run locally
for both supported revisions (except the Director launch, which used the pinned revision). A real Wizard101 client
round trip remains unverified because no separate safe client setup was available. The installed client and its saves
were not modified or launched. When a separate setup is available, validate character-select and in-game replies,
session lifetime, and client-visible veto behavior there.
