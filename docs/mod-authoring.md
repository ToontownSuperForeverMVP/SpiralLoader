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

## 5. Imlight hooks (integration API 1.0.0)

Reference `Imlight.CoreLib` (via `ImlightDirectory`) and use `Imlight.CoreLib.Modding`:

| Hook | Kind | Notes |
| --- | --- | --- |
| `ServerStartingEvent`, `ServerResourcesLoadedEvent`, `ServerStartedEvent`, `ServerStoppingEvent` | event (Api) | Published by the Director |
| `LootGrantingEvent` | event | Change `Results` (items, gold, experience) before loot is granted |
| `ImlightRegistries.GameMessageServices` | registry of `Type` | Extra per-session game services |
| `ImlightRegistries.LoginMessageServices` | registry of `Type` | Extra per-session login services |
| `CommonRegistries.Commands` | registry of `ModCommand` | Chat commands (`.name`), tried after built-in commands; `OperatorOnly` (default) requires Administrator |

Imlight's `MessageService` base class is `internal`, so a separately compiled mod cannot derive from it yet.
See [audit.md §2](audit.md#access-to-imlight-internals) and the next milestone.

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
