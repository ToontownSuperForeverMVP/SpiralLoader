# Writing a SpiralLoader mod

A mod is a folder containing `spiralmod.json` and, optionally, assemblies and data. Install it by copying the
folder into the server's `mods/` directory and restarting the server.

```
mods/
  my-mod/
    spiralmod.json
    MyMod.dll
    MyMod.deps.json      (generated)
    assets/…             (optional data)
```

## 1. Project setup

Inside this repository (see `examples/HelloSpiral`):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="..\..\build\SpiralLoader.Mod.props" />
  <PropertyGroup>
    <SpiralModId>my-mod</SpiralModId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\SpiralLoader.Api\SpiralLoader.Api.csproj" Private="false" />
  </ItemGroup>
  <Import Project="..\..\build\SpiralLoader.Mod.targets" />
</Project>
```

Outside the repository, set `SpiralLoaderApiPath` to a `SpiralLoader.Api.dll` and `SpiralModOutputRoot` to
where the mod folder should be assembled. To use Imlight types, set `ImlightDirectory` to a built (patched)
Imlight server directory; Imlight, Akka and Imcodec are then referenced without being copied.

**Never ship assemblies the host provides** (`SpiralLoader.Api`, Imlight, Akka, Imcodec, framework). The
`.props/.targets` take care of this; if a copy slips in, the host's copy is used and a warning is logged.

## 2. Manifest

Minimal:

```json
{
  "schemaVersion": 1,
  "id": "my-mod",
  "version": "1.0.0",
  "entrypoints": { "server": ["MyMod.MyModEntry"] },
  "assemblies": ["MyMod.dll"],
  "depends": { "spiralloader": ">=0.1.0 <0.2.0", "imlight": "^1.0.0" }
}
```

Full reference: [architecture.md](architecture.md#manifest-spiralmodjson-schema-1). Depend on `imlight` by
version when you only use SpiralLoader hooks; add `compatibility.imlightCommits` when you rely on Imlight
internals, and `compatibility.clientRevisions` when your data targets a specific client build.

## 3. Entrypoint

```csharp
using SpiralLoader.Api;
using SpiralLoader.Api.Events;
using SpiralLoader.Api.Logging;

public sealed class MyModEntry : ISpiralMod {
    public void Initialize(IModContext context) {
        context.Logger.Info($"{context.Manifest.Name} loading");
        context.Events.Subscribe<ServerStartedEvent>(_ => context.Logger.Info("server is up"));
    }

    public void Shutdown() { }   // optional
}
```

Rules:

- Subscribe and register **only during `Initialize`**. Afterwards registrations are closed.
- If `Initialize` throws, the mod is marked failed and everything it registered is removed. Under the default
  failure policy the server then refuses to start, which is deliberate: running half a mod can corrupt saves.
- Use `context.Environment.DataDirectory` for persistent data (scoped to the active profile) and
  `ConfigDirectory` for user-editable settings. Treat `ModDirectory` as read-only.

## 4. Events and threading

Handlers run synchronously on the thread that published the event. In Imlight that is an Akka.NET actor
processing a message, so:

- never block (no `.Result`, no `Thread.Sleep`, no synchronous I/O on hot paths);
- do not keep references to the event's objects after returning; they belong to the actor;
- hand off long work with `IActorRef.Tell` or a `Task`, and re-enter actors only through messages.

Change filter-style events by setting their properties. Call `Cancel()` on a `CancellableEvent` to stop
lower-priority handlers and the host's default action. Use `EventPriority.Monitor` to observe the final
state without changing it.

## 5. Imlight hooks (integration API 1.2.0)

Reference `Imlight.CoreLib` (via `ImlightDirectory`) and use `Imlight.CoreLib.Modding`:

| Hook | Kind | Notes |
| --- | --- | --- |
| `ServerStartingEvent`, `ServerResourcesLoadedEvent`, `ServerStartedEvent`, `ServerStoppingEvent` | event (Api) | Published by the Director |
| `LootGrantingEvent` | event | Change `Results` (items, gold, experience) before loot is granted |
| `CharacterListSentEvent` | event (1.1) | After the character list reaches the client; `Send(IMessage)` writes to the client's login socket |
| `GameServerSelectingEvent` | cancellable event (1.1) | Route a player to a realm or veto the transition after they pick a character |
| `ImlightRegistries.ClientProcessJobs` | registry of `uint` (1.1) | Claim client process `JobID`s so Imlight's `MinigameService` leaves them alone; since 1.2 their replies reach only your services |
| `ModMessageService` | base class (1.2) | Write per-session message services in a separately compiled mod (`[MessageHandler]`, `SendToSocket`, `GetSocketAccount`, `StartClientProcess`, `OnSessionEnded`) |
| `ImlightRegistries.GameMessageServices` | registry of `Type` | Extra per-session game services (`ModMessageService` types since 1.2) |
| `ImlightRegistries.LoginMessageServices` | registry of `Type` | Extra per-session login services (`ModMessageService` types since 1.2) |
| `CommonRegistries.Commands` | registry of `ModCommand` | Chat commands (`.name`), tried after built-in commands; `OperatorOnly` (default) requires Administrator |

### Hooks added in 1.1.0

Mods that use them declare `"imlight": "^1.1.0"`; on a server patched only to 1.0.x they are rejected with
`dependency.version_mismatch` instead of failing at run time.

**`CharacterListSentEvent`** (`Account`, `Send(IMessage)`). Published by the login server's `CharacterService` once
`MSG_CHARACTERLIST` has been sent, which is the moment the character select screen is usable. It is not
published when the account is unknown or a character could not be serialized (the client then got an error
instead of a list). `Send` is only valid while the handler runs. Typical use: start a client process.

```csharp
context.Events.Subscribe<CharacterListSentEvent>(e =>
    e.Send(new GAME_5_PROTOCOL.MSG_START_CLIENT_PROCESS { OwnerGID = 0, JobID = MenuJobId, ScriptName = "MyMod/Menu.lua" }));
```

**`GameServerSelectingEvent`** (`Account`, `Character`, `RealmName`, cancellable). Published by
`GameTransitionService` after the character was validated and before Imlight asks for a game server.

- Default (`RealmName` null or empty): Imlight's own best-server choice, exactly as without mods.
- Set `RealmName` to the *exact* realm name of a configured game server: the player is sent there (the lookup is
  the one `MSG_QUERYREALMSERVER` already does). A name that matches no game server logs an error and
  rejects the transition; a mod never silently sends players somewhere else.
- `Cancel()` rejects the transition: the client receives `MSG_CHARACTERSELECTED` with `Error = 1`. What happens to
  the session then depends on the Imlight revision, because the hook reuses the service's own error path: on the
  pinned `b0798716` that path also closes the session; on `quality-assurance` `2a16010e` it only sends the error
  and the session stays open. The verification harness asserts both. Lower-priority handlers do not run, so a
  veto cannot be overridden by a later handler.

**`ImlightRegistries.ClientProcessJobs`.** A mod that starts a client process (`MSG_START_CLIENT_PROCESS`) with
its own `JobID` registers that id during `Initialize`:

```csharp
context.Registries.Get(ImlightRegistries.ClientProcessJobs).Register("menu", 770101);
```

Claimed ids (a) are unique across mods: a second claim of the same id fails the second mod's `Initialize` with
`init.failed` and rolls it back; `0` is reserved; (b) are ignored by `MinigameService`, which otherwise receives
every `MSG_MESSAGE_PROCESS` through Imlight's fan-out dispatch, tries to decode it as minigame traffic and logs
an error; and (c) are never allocated by `ProcessSupervisor` to a minigame. Since 1.2.0 (d) the client's replies
for a claimed id reach only the `ModMessageService`s of the mod that claimed it (next section).
`ImlightModding.IsClientProcessJobClaimed(uint)` tells you whether an id is claimed. Pick ids well away from the
random range Imlight allocates (`1 … int.MaxValue`) only if you do not claim them; claimed ids are safe anywhere.

### Hooks added in 1.2.0: mod message services and client process replies

Mods that use them declare `"imlight": "^1.2.0"`.

Imlight's own `MessageService` base class is `internal`, so a separately compiled mod cannot derive from it.
Derive from the public **`Imlight.CoreLib.Modding.ModMessageService`** instead and register the type in
`ImlightRegistries.LoginMessageServices` (character select, where `CharacterListSentEvent` runs) or
`GameMessageServices` (in-game). The type needs a public parameterless constructor; the registry validator
rejects anything else in your `Initialize`.

```csharp
public sealed class MyMod : ISpiralMod {
    public const uint MenuJob = 770101;

    public void Initialize(IModContext context) {
        context.Registries.Get(ImlightRegistries.ClientProcessJobs).Register("menu", MenuJob);
        context.Registries.Get(ImlightRegistries.LoginMessageServices).Register("menu_replies", typeof(MenuReplies));
        context.Events.Subscribe<CharacterListSentEvent>(e =>
            e.Send(new GAME_5_PROTOCOL.MSG_START_CLIENT_PROCESS { JobID = MenuJob, ScriptName = "MyMod/Menu.lua" }));
    }
}

public sealed class MenuReplies : ModMessageService {
    [MessageHandler(typeof(GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS))]
    private void OnMenuReply(GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS reply) {
        var account = GetSocketAccount();
        // ... act on reply.Message, then for example close the menu:
        SendToSocket(new GAME_5_PROTOCOL.MSG_KILL_CLIENT_PROCESS { JobID = reply.JobID });
    }
}
```

How it works and what you can rely on:

- **One instance per session.** Imlight creates your service when a session starts, inside an internal host
  service that is a real `MessageService` child of the session's `SessionActor`. The session's own dispatch table
  routes every message type your `[MessageHandler]` methods declare (`Imlight.CoreLib.Shared.Networking.MessageHandlerAttribute`,
  the attribute Imlight's services use) to that host, which calls your handlers. A handler may take the message
  or no parameter. Declare handlers on the registered type; on a base class they must not be `private`. As for
  Imlight's own services, the session routes by the packet's exact type, so declare concrete message types.
- **Akka threading.** Handlers run on the host actor's thread, one message at a time, like Imlight's own
  services. Do not block, do not share the instance across threads, keep per-session state in the instance.
- **Helpers.** `SendToSocket(IMessage)` sends to this session's client. `GetSocketAccount()` returns the
  session's account (one ask to the session's account service, then cached). `SessionId` and `OwnerModId` identify
  the instance. `StartClientProcess(jobId, script)` sends `MSG_START_CLIENT_PROCESS` for an id **your** mod
  claimed and throws `InvalidOperationException` for any other id. The helpers are not available in the
  constructor.
- **Client process routing.** `MSG_MESSAGE_PROCESS` and `MSG_CLIENT_PROCESS_TERMINATED` carry a `JobID`. They
  reach only the services of the mod that claimed that id; an unclaimed id reaches no mod service and stays with
  `MinigameService`. Two mods can therefore both declare a `MSG_MESSAGE_PROCESS` handler without seeing each
  other's replies, and a mod never sees minigame traffic.
- **Faults.** If your constructor throws, the service is logged against your mod and left out of that session;
  if a handler throws, the error is logged and the next message is handled normally. Neither stops the session
  or other services (on both supported Imlight revisions, whose supervision strategies differ).
- **Session end.** Override `OnSessionEnded()` to release per-session state. It runs during Imlight's pre-dispose
  step, while the session's other services still run; Imlight waits at most two seconds for all services.
- **Without mods.** With no `ModMessageService` registered the host is not added and the service factories return
  their built-in sets; a disabled mod's registrations do not exist, so its job ids go back to `MinigameService`.

Other messages fan out as in Imlight: if your service and a built-in service both handle a message type, both
receive it.

Not available yet (planned, see [next-milestone.md](next-milestone.md)): zone entity template, NPC stat, combat
spell and world teleport list hooks, and zone component contributions.

Requirement and result handlers deriving from Imlight's public `BaseRequirementHandler<T>` /
`BaseResultHandler<T>` are discovered automatically, because mods load before Imlight scans assemblies. If
Imlight already handles the same type, which handler wins is unspecified.

## 6. Data (planned)

Declare data now so it is validated and planned:

```json
"assets": [
  { "kind": "wad", "source": "assets/root", "target": "GameData/Root.wad" },
  { "kind": "spiraldb", "source": "spiraldb", "target": "" }
]
```

Applying WAD and SpiralDB overlays is not implemented yet. `SpiralLoader.Host --plan-assets` shows the merged
plan and conflicts. Ship only original content: never redistribute Wizard101 files.

## 7. Trust

Mods are trusted native code with full access to the server process. SpiralLoader is not a sandbox.
