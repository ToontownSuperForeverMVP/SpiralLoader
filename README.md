# SpiralLoader

**An experimental, Fabric-inspired mod loader and public mod API for [Imlight](https://github.com/Revive101/Imlight), an open-source private-server framework for a legacy online game.**

SpiralLoader gives server mods a defined entry point and API, then connects that API to Imlight through a small, maintained patch series. It is early development software: the server-side loader and the 1.2.0 `ModMessageService` bridge have passed real Windows integration testing, but a complete client gameplay session has **not** been verified.

SpiralLoader is not affiliated with the upstream server project or any game publisher. It does not include or provide client files, game assets, extracted game data, or Imcodec's client-derived generator inputs.

## What it provides

- **Manifest-based mods.** A `spiralmod.json` manifest declares a mod's identity, entry point, assemblies, dependencies, compatibility pins, and asset declarations.
- **Dependency-aware loading.** Semantic version requirements, optional recommendations, incompatibilities, explicit load ordering, cycle detection, and deterministic resolution.
- **A small public API.** Lifecycle and gameplay events, commands, namespaced registries, and mod logging are exposed through `SpiralLoader.Api`.
- **Per-mod loading contexts.** Each mod gets its own .NET `AssemblyLoadContext`; dependencies share types as needed. Failed initialization rolls back that mod's registrations and is reported according to the configured failure policy.
- **Client-process messages.** With the Imlight 1.2.0 patch series, a separately built mod can claim client-process job IDs, start processes, and receive only the replies belonging to its claims through `ModMessageService`.
- **A headless development host.** Build and exercise mods without an Imlight server.

Mods are native .NET code and run inside the server process with its permissions. Load contexts and fault handling help organize mods and contain ordinary failures; **they are not a security sandbox**. Mods load at startup and changes require a server restart.

Asset manifests and deterministic overlay planning exist, but SpiralLoader does not currently write or serve client WAD overlays or SpiralDB overlays. Spiral Shuffle is an example of a possible third-party integration; no port is planned or promised by this project.

## Architecture and documentation

The code is split into a dependency-free mod API, a host-agnostic runtime, an Imlight bootstrap, and a headless host. Imlight-specific hook call sites live in the patch series under `integration/imlight/patches`; the runtime does not reference Imlight assemblies.

- [Architecture and runtime behavior](docs/architecture.md)
- [Mod authoring guide](docs/mod-authoring.md)
- [Building, testing, and Imlight setup](docs/building.md)
- [Imlight patch series and verification details](integration/imlight/README.md)
- [Imlight and modding compatibility research](docs/audit.md)
- [Development roadmap](docs/next-milestone.md)
- [Milestone 3 Windows test report](MILESTONE3_LOCAL_TEST_RESULTS.md)

## Supported environment and Imlight revisions

- .NET 10 SDK; the repository pins its SDK feature band in `global.json`.
- Imlight integration API **1.2.0**.
- Patch application and automated integration verification currently cover Imlight commit `b07987168fcfa2d123a5d78df90ca0c76405e427` and audited `quality-assurance` commit `2a16010e59b95cc449d8e21542fbf38440e06bcf`.
- Windows is the primary platform. CI also builds and verifies the patch series on Ubuntu.

The Imlight patch verifiers do not require game data. A full Imlight CoreLib/Director build needs Imcodec generator inputs matching the server's client revision. Those inputs are not distributed here and must not be committed to this repository. See [the build guide](docs/building.md) for the distinction between the automated harness and a full local Imlight build.

## Build and test SpiralLoader

Install the .NET 10 SDK, then run from the repository root:

```powershell
dotnet build SpiralLoader.sln -c Release
dotnet test SpiralLoader.sln -c Release --no-build
```

The current suite contains 153 automated tests. The build also places ready-to-load example mod folders under `artifacts/mods/`.

## Build and run HelloSpiral

`HelloSpiral` demonstrates a manifest, a server entry point, lifecycle events, a command, a mod API, and an asset declaration.

```powershell
dotnet build examples/HelloSpiral/HelloSpiral.csproj -c Release
dotnet run --project src/SpiralLoader.Host -- --mods artifacts/mods --run "hello world" --plan-assets
```

To load it in Imlight, build and patch a separate Imlight checkout as described in the [Imlight build guide](docs/building.md), then copy `artifacts/mods/hello-spiral` into the `mods/` folder beside `Imlight.Director`. The example asset is a declaration for planning; it is not merged into or served as a client WAD by this release.

## Apply and verify Imlight patches

From the repository root, the setup scripts create an isolated checkout under ignored `external/`, apply the maintained patch series, and run the verification harness:

```powershell
pwsh integration/imlight/Setup-Imlight.ps1
pwsh integration/imlight/verify/Verify-Patch.ps1
pwsh integration/imlight/verify/Verify-Patch.ps1 -Commit 2a16010e59b95cc449d8e21542fbf38440e06bcf
```

On Linux or macOS, use `integration/imlight/setup-imlight.sh` and `integration/imlight/verify/verify-patch.sh` (set `IMLIGHT_COMMIT` to verify the QA revision). The patch series has 13 commits and is tested on both supported revisions. The harness uses stubs for generated Imcodec and selected Imlight types; the Windows milestone testing additionally built both complete patched Imlight revisions using real locally available generator inputs and exercised the bridge against the resulting assemblies.

## Verification status

The 1.2.0 server integration passed the full 153-test SpiralLoader suite, both patch verifiers on both revisions, full patched Imlight Release builds with real generated types on Windows, external-mod compilation against the real Imlight assemblies, generated message-type checks, synthetic message dispatch and disposal scenarios, and an isolated Director startup with the external mod. Vanilla startup with SpiralLoader disabled was also checked in the isolated Director.

A complete client gameplay session and client-side message round trip have **not** been verified. No installed client was launched or modified. This project is experimental and is not a production-readiness claim.

### Known dependency advisory

The audited Imlight revisions use Akka.Remote versions below 1.5.52. Upstream advisory [GHSA-jhpv-4q4f-43g5 / CVE-2025-61778](https://github.com/advisories/GHSA-jhpv-4q4f-43g5) describes a critical certificate-authentication flaw affecting TLS-enabled Akka.Remote connections before 1.5.52. This is an upstream dependency issue; review the advisory and Imlight's deployment before exposing remoting endpoints. More detail is in [docs/audit.md](docs/audit.md).

## Roadmap

The roadmap focuses on stable extension APIs, broader server gameplay hooks, asset and database overlays, developer tooling, and compatibility testing. SpiralLoader is an independent, general-purpose modloading framework; third-party projects may choose to integrate with it. See [the development roadmap](docs/next-milestone.md).

## Contributing

Contributions are welcome. Start with [CONTRIBUTING.md](CONTRIBUTING.md), read the relevant design and build docs, and include focused verification results. Do not submit client-derived inputs, game assets, proprietary files, credentials, or local build output.

## License and acknowledgments

SpiralLoader's original project code is offered under **AGPL-3.0-only** (`SPDX-License-Identifier: AGPL-3.0-only`); see [LICENSE](LICENSE). The Imlight patch series is based on [Revive101's Imlight](https://github.com/Revive101/Imlight) and retains its upstream copyright and license notices. Third-party dependencies retain their own licenses. No copyright ownership over third-party code is claimed here. See [third-party notices](THIRD-PARTY-NOTICES.md) for the licensing boundaries.

Thanks to **Revive101** for Imlight and **ljste** for [Spiral Shuffle](https://github.com/ljste/spiral-shuffle), whose architecture and extension needs informed compatibility research. Spiral Shuffle remains an independent project; any future integration would be undertaken by its maintainer or another contributor, with no commitment implied.

## AI-Assisted Development

SpiralLoader is directed and maintained by a human, with substantial AI assistance from **Claude Opus 5.5**, **Claude Sonnet 5.5**, and **OpenAI GPT-6 Luna High** for implementation, debugging, testing, and documentation. AI assistance contributed substantially to the work; this project does not claim that every line was independently human-authored. Maintainers review and remain responsible for the project and its releases.
