# Development roadmap

SpiralLoader is an experimental, independent, general-purpose, Fabric-inspired modloading framework for Imlight. The current milestone establishes a mod API and a tested server-side bridge. A complete client gameplay session has not been verified. Spiral Shuffle is an independent project and is not a SpiralLoader deliverable; it may be one example of a third-party integration if its maintainer or another contributor chooses to pursue it.

## Completed: Imlight integration API 1.2.0

The maintained 13-commit patch series currently provides:

- Startup loading and lifecycle events.
- Loot, chat-command, character-list, and game-server-selection hooks.
- Tolerant discovery of extension types.
- A client-process job registry and per-mod reply routing through `ModMessageService`.

The series applies and its automated harness passes against the pinned Imlight commit `b07987168fcfa2d123a5d78df90ca0c76405e427` and audited QA commit `2a16010e59b95cc449d8e21542fbf38440e06bcf`. Windows integration testing completed full patched Release builds with real Imcodec-generated types for both revisions, external-mod builds against the real Imlight assemblies, synthetic dispatch and disposal scenarios, and isolated Director startup with mods enabled and disabled. A real client gameplay session remains unverified; see [the integration verification record](../MILESTONE3_LOCAL_TEST_RESULTS.md).

## Official roadmap

1. Stabilize extension APIs, document compatibility contracts, and improve mod compatibility checks.
2. Add zone entity template resolution and NPC statistic hooks.
3. Add combat, spell, and gameplay extension events.
4. Add zone component registration and teleportation hooks.
5. Implement client Lua and WAD asset overlay support.
6. Implement SpiralDB overlays and asset conflict management.
7. Improve mod packaging, installation, diagnostics, and developer tooling.
8. Expand client/server compatibility and regression testing using safe, separate client setups.
9. Support third-party mod developers with stable APIs, examples, authoring tools, and migration guidance.

These are project directions, not claims that the features already exist. Spiral Shuffle informed some compatibility research and remains an independent project; no agreement or commitment from its maintainer is implied. SpiralLoader will not undertake a port as an official acceptance requirement.

## Verification boundaries

The server-side loader and `ModMessageService` bridge passed real Windows integration tests against the two supported Imlight revisions. The full automated suite contains 153 tests. A complete client gameplay session has not been verified, and client WAD or SpiralDB overlays are not implemented. Keep those limits explicit in release notes and compatibility claims.
