# Next milestone: Spiral Shuffle server half as a SpiralLoader mod

Goal: the server-side code of Spiral Shuffle (`RandomizerRuntime`, `CreatureRandomization`,
`EnemyInspectorService`, `EnemyInspectorData`, `RandomizerLevelRequirement`, and the behaviour of its 27
`prepare_source.py` replacements) runs as a separately compiled mod on patched Imlight, with **no source
copying or string replacement**. Client assets keep using Spiral Shuffle's existing pipeline for now.

## Remaining work, in order

### A. Hook points (new commits in `integration/imlight/patches`)

Add one commit per hook. Each must be a no-op when nothing subscribes (`HasSubscribers` guard), use only
public Imlight types in its event, and be listed in `docs/mod-authoring.md` and `docs/audit.md`.

**Status (milestone 2, integration API 1.1.0):** rows 1, 2 and 7 below are implemented (patches 0007-0010):
`CharacterListSentEvent`, `GameServerSelectingEvent` and `imlight:client_process_jobs`. They were verified
by the cloud harness (real call-site files compiled against Imcodec stubs and driven by scenarios, on both
supported Imlight revisions) but **not** by a full Imlight build or a client session; see
[integration/imlight/README.md](../integration/imlight/README.md#verification-status). The rest of the table
is still to do; bump the API minor version (1.2.0) with the next batch.

| Event / registry | Call site (pinned source) | Replaces Shuffle change |
| --- | --- | --- |
| ✅ `CharacterListSentEvent` (account, `Send(IMessage)` helper) | `Login/Services/CharacterService.cs`, after `SendToSocket(new LOGIN_7_PROTOCOL.MSG_CHARACTERLIST())` | menu Lua start |
| ✅ `GameServerSelectingEvent` (cancellable, account, character, chosen realm) | `Login/Services/GameTransitionService.cs`, before `MSG_GETBESTSERVER` | realm by mode |
| `ZoneEntityTemplateResolvingEvent` (template, template id, zone path; settable template + client template id) | `Game/Zone/Core/ZoneEntity.cs`, before `AutoAttachComponents()` and where `m_templateID` is serialized | creature shuffle (2 hunks) |
| `NpcStatsInitializingEvent` (stats, starting health, zone path, is monster) | `Game/Zone/Components/NpcComponent.cs` (3 hunks) | creature stat rolls |
| `CombatSpellResolvingEvent` (template id, account/participant, settable id) | `Game/Combat/CombatDeck.cs` / `CombatDuelSubCircle.cs` | spell randomization (6 hunks) |
| `WorldTeleportListBuildingEvent` (mutable world list) | `Game/Zone/Components/WorldTeleportDoorComponent.cs` | file overwrite |
| ✅ Client-process job registry: mods claim `JobID`s; `MinigameService` ignores claimed ids | `Game/Services/MinigameService.cs` | job-id special case |
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

## Recommended next steps

1. **Local smoke test of 1.1.0 (Windows, needs Imcodec generator inputs and a client).** Build the patched
   Imlight (`Setup-Imlight.ps1`), which also confirms the compile-unverified assumptions listed in the
   integration README (`MSG_MESSAGE_PROCESS.JobID` is `uint`, generated messages are `IMessage`). Then run a
   throwaway mod that, on `CharacterListSentEvent`, starts a trivial client Lua process, claims its job id and
   logs the reply; check the log shows no `Failed to deserialize message process` and that cancelling in
   `GameServerSelectingEvent` shows the client an error.
2. **Section B before the port**: decide how a mod derives from `MessageService` (it is `internal`), because
   the port's inspector service, menu reply handler and `RandomizerLevelRequirement` all need it. Prove the
   chosen route with a test mod in the harness.
3. **Remaining hooks (section A)**, in the order the port needs them: `ZoneEntityTemplateResolvingEvent`,
   `NpcStatsInitializingEvent`, zone component contributions, `CombatSpellResolvingEvent`,
   `WorldTeleportListBuildingEvent`. The harness cannot compile the zone and combat files (deep generated-type
   dependencies); expect to rely on small, reviewable patches plus a local full build.

## Hand-off prompt for the next session

> You are continuing SpiralLoader (read `CLAUDE.md`, then `docs/next-milestone.md` and `docs/audit.md`).
> Milestone 2 added integration API 1.1.0 (`CharacterListSentEvent`, `GameServerSelectingEvent`,
> `imlight:client_process_jobs`). Implement section B first: choose and prove, with a test mod in the
> verification harness, how a separately compiled mod derives from Imlight's internal `MessageService`
> (a public `ModMessageServiceBase` added by the patch series, or an `IgnoresAccessChecksTo` publicized
> reference; prefer the former), without exposing more of Imlight than needed. Then add
> `ZoneEntityTemplateResolvingEvent` and `NpcStatsInitializingEvent` as one commit each in the patch series
> (follow "Changing the Imlight patch series" in `CLAUDE.md`, including `regen-patches.sh`, both verify
> scripts for the pinned commit and `2a16010e59b95cc449d8e21542fbf38440e06bcf`, and the 1.2.0 version bump).
> Be explicit about which call sites the harness compiled and which remain compile-unverified. Do not port
> Spiral Shuffle code yet.
