# Contributing to SpiralLoader

SpiralLoader is experimental software. Contributions should keep its public API small, preserve unmodded Imlight behavior, and state clearly which parts were compiled or exercised.

## Before opening a pull request

1. Read [the architecture](docs/architecture.md), [mod-authoring guide](docs/mod-authoring.md), and [build guide](docs/building.md) relevant to your change.
2. Keep changes focused and explain the user-visible or mod-author-visible effect.
3. Run the relevant checks. For runtime/API changes, run `dotnet build SpiralLoader.sln -c Release` and `dotnet test SpiralLoader.sln -c Release --no-build`. If `ApiCompatibilityTests` or an Imlight baseline check fails, follow [docs/api-compatibility.md](docs/api-compatibility.md) (SemVer table, changelog, new baseline) instead of just regenerating the file. For Imlight patch changes, also run both patch verifiers against the pinned and audited QA commits as described in [integration/imlight/README.md](integration/imlight/README.md).
4. For Imlight hooks, preserve upstream style and copyright headers, keep hooks inert when there are no subscribers, and do not add Imlight types to the host-agnostic runtime.
5. Disclose substantial AI assistance in the pull request description when it materially shaped the implementation, debugging, tests, or documentation. A human contributor must review and take responsibility for submitted changes.

## Do not submit

- Wizard101 client files, game assets, extracted game data, Imcodec generator inputs, or other proprietary or client-derived material.
- Credentials, private logs, local machine paths, build output, caches, or generated binaries.
- Third-party code without its required license and attribution.

SpiralLoader mods are trusted native code, not sandboxed code. Avoid describing error isolation or per-mod load contexts as a security boundary.

## Licensing

By contributing original work, you agree that it may be distributed under the repository's default **AGPL-3.0-only** license. Preserve copyright and license notices for upstream and third-party material. Do not relicense or remove notices from code you did not author.
