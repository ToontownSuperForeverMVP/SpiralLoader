# Development roadmap

SpiralLoader is experimental. The current milestone establishes a mod API and a tested server-side bridge; it does not include a Spiral Shuffle port or client asset overlay implementation.

## Completed: Imlight integration API 1.2.0

The maintained 13-commit patch series currently provides:

- Startup loading and lifecycle events.
- Loot, chat-command, character-list, and game-server-selection hooks.
- Tolerant discovery of extension types.
- A client-process job registry and per-mod reply routing through `ModMessageService`.

The series applies and its automated harness passes against the pinned Imlight commit `b07987168fcfa2d123a5d78df90ca0c76405e427` and audited QA commit `2a16010e59b95cc449d8e21542fbf38440e06bcf`. Windows milestone testing also completed full patched Release builds with real Imcodec-generated types for both revisions, external-mod builds against the real Imlight assemblies, synthetic dispatch and disposal scenarios, and isolated Director startup with mods enabled and disabled. A real Wizard101 client session remains unverified; see [the integration verification record](../MILESTONE3_LOCAL_TEST_RESULTS.md).

## Next: add narrow hooks for server-side mod ports

The next integration API increment should add the remaining public surface needed to port Spiral Shuffle's server behavior as a separately compiled mod, without copying Imlight source or replacing source text:

1. Inventory the remaining Imlight internals the port needs, including `NumericRequirement` and zone component factories. Prefer narrow public wrappers in `Imlight.CoreLib.Modding`; verify each from a separately compiled mod.
2. Add `ZoneEntityTemplateResolvingEvent` and `NpcStatsInitializingEvent` for creature templates and stats.
3. Add zone component contributions to replace the inspector component integration.
4. Add `CombatSpellResolvingEvent` and `WorldTeleportListBuildingEvent` for spell and teleport randomization.
5. For each patch, preserve no-mod behavior, update the integration API version and authoring docs, run both verification scripts against both supported revisions, and perform a full Windows build with matching real generator inputs.
6. Port the relevant Spiral Shuffle server behavior into a standalone mod and compare deterministic outcomes with its existing tests.

Spiral Shuffle remains a target for this work, not a current SpiralLoader feature. Credit and licensing for any upstream code or behavior must be handled before porting; see [the audit](audit.md).

## Later work

- Implement WAD overlay writing and integrate overlays with Imlight's patch server.
- Add SpiralDB overlays and per-profile mod/configuration sets.
- Package the public API and MSBuild mod-authoring support for easier consumption.
- Validate client-visible behavior with a separate, safe Wizard101 client setup before advertising a playable alpha.
