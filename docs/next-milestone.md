# Next milestone: Spiral Shuffle server half as a SpiralLoader mod

Goal: the server-side code of Spiral Shuffle (`RandomizerRuntime`, `CreatureRandomization`,
`EnemyInspectorService`, `EnemyInspectorData`, `RandomizerLevelRequirement`, and the behaviour of its 27
`prepare_source.py` replacements) runs as a separately compiled mod on patched Imlight, with **no source
copying or string replacement**. Client assets keep using Spiral Shuffle's existing pipeline for now.

## Remaining work, in order

### A. Hook points (new commits in `integration/imlight/patches`, integration API → 1.1.0)

Add one commit per hook. Each must be a no-op when nothing subscribes (`HasSubscribers` guard), use only
public Imlight types in its event, and be listed in `docs/mod-authoring.md` and `docs/audit.md`.

| Event / registry | Call site (pinned source) | Replaces Shuffle change |
| --- | --- | --- |
| `CharacterListSentEvent` (account, `Send(IMessage)` helper) | `Login/Services/CharacterService.cs`, after `SendToSocket(new LOGIN_7_PROTOCOL.MSG_CHARACTERLIST())` | menu Lua start |
| `GameServerSelectingEvent` (cancellable, account, chosen realm) | `Login/Services/GameTransitionService.cs`, before `MSG_GETBESTSERVER` | realm by mode |
| `ZoneEntityTemplateResolvingEvent` (template, template id, zone path; settable template + client template id) | `Game/Zone/Core/ZoneEntity.cs`, before `AutoAttachComponents()` and where `m_templateID` is serialized | creature shuffle (2 hunks) |
| `NpcStatsInitializingEvent` (stats, starting health, zone path, is monster) | `Game/Zone/Components/NpcComponent.cs` (3 hunks) | creature stat rolls |
| `CombatSpellResolvingEvent` (template id, account/participant, settable id) | `Game/Combat/CombatDeck.cs` / `CombatDuelSubCircle.cs` | spell randomization (6 hunks) |
| `WorldTeleportListBuildingEvent` (mutable world list) | `Game/Zone/Components/WorldTeleportDoorComponent.cs` | file overwrite |
| Client-process job registry: mods claim `JobID`s; `MinigameService` ignores claimed ids | `Game/Services/MinigameService.cs` | job-id special case |
| Zone component contributions (`imlight:zone_components`: factory predicate + component type) | `ZoneEntityComponentRegistry` | `CombatDuelComponent` inspector handler |

Upstream (not mod hooks): send Spiral Shuffle's `QuestService` hidden-quest and `TutorialService` hand-off
fixes to Imlight as ordinary bug-fix PRs, with Imlight's AI-disclosure requirements (CONTRIBUTING §2).

### B. Access to internals

Inventory every `internal` Imlight member the ported code uses (compile the port and read the errors). For each,
prefer (1) a public member on the event or a SpiralLoader-owned public wrapper in `Imlight.CoreLib.Modding`
added by the patch series; fall back to (2) an `IgnoresAccessChecksTo("Imlight.CoreLib")` publicized
reference, which must first be proven to work with a test mod in the verification harness.
`MessageService` is the critical one: decide between making it public upstream (with the members it exposes)
or a public `ModMessageServiceBase` defined inside CoreLib by the patch.

### C. The port

New repository or `examples/SpiralShuffle` (respect its AGPL-3.0 licence and credit ljste): manifest pinned to
`b07987168fcfa2d123a5d78df90ca0c76405e427` and `r806919.Wizard_1_610`; `Config/Randomizer.json` read from
`context.Environment.ConfigDirectory`; mode file under `DataDirectory`; realm names from configuration.

### D. Verification

Extend `integration/imlight/verify` stubs for each new hook's call-site types where practical; then a full local
Windows build with the Imcodec generator inputs, run the server with the port, and repeat Spiral Shuffle's own
`tests/Program.cs` checks against the mod.

## Later milestones

- WAD overlay writer (port `pack_assets.py` to C# on `Imcodec.Wad`), `LatestFileList` rewrite, serving through
  Imlight's patch server; SpiralDB overlays; per-profile mod sets and INI overlays.
- Asset generation stage (Spiral Shuffle's `asset-tool`) as a `tool` entrypoint kind run by a CLI.
- Explicit override precedence for requirement/result handlers.
- NuGet packages for `SpiralLoader.Api` and the mod build props.

---

## Hand-off prompt for Sonnet 5.5

> You are continuing SpiralLoader (read `CLAUDE.md` first, then `docs/next-milestone.md` and `docs/audit.md`).
> Implement section A, first three rows only: `CharacterListSentEvent`, `GameServerSelectingEvent`, and the
> client-process job registry (including the `MinigameService` change). For each: add a commit on top of the
> series in a checkout made by `integration/imlight/setup-imlight.sh` (pinned commit
> `b07987168fcfa2d123a5d78df90ca0c76405e427`), keep the event types in `Imlight.CoreLib.Modding` using only public
> Imlight types, guard every call site with `HasSubscribers`, bump `ImlightModding.IntegrationApiVersion` to
> `1.1.0`, regenerate `integration/imlight/patches` with `git format-patch`, and confirm the series still applies
> to upstream `quality-assurance` HEAD. Extend `integration/imlight/verify` so the harness compiles each touched
> file it can (add stubs; do not stub the file under test) and asserts the new events fire in a scenario you
> drive from the stub login server. Update `docs/mod-authoring.md`, `docs/audit.md` (mark rows implemented) and
> `CLAUDE.md`. Run `dotnet test SpiralLoader.sln` and both verify scripts (bash, and `pwsh` if available) and
> report exactly which files were compiled by the harness and which call sites remain compile-unverified
> (Imlight.CoreLib cannot be fully built without the Imcodec generator inputs). Do not port Spiral Shuffle code
> yet and do not change the loader's public API without updating its tests.
