# Prebuilt fixture sources

Sources of **binaries that are committed** under `tests/SpiralLoader.Tests/Prebuilt/` and deliberately not rebuilt by
`dotnet build SpiralLoader.sln` or the test run. They stand in for mods that were compiled earlier, to prove those keep
loading against the current runtime without a rebuild (see
[docs/compatibility-testing.md](../../docs/compatibility-testing.md#the-prebuilt-fixture)).

| Source | Output | Built against |
| --- | --- | --- |
| `ApiTourMod` | `Prebuilt/api-0.1.0/api-tour` | `SpiralLoader.Api` assembly version `0.1.0.0` |

Rebuild only to move the compatibility target on purpose: `dotnet build tests/fixtures/ApiTourMod -c Release`. For a new
API assembly version add a new `Prebuilt/api-<version>/` folder and keep the old one so old binaries stay covered.
Fixtures contain original code only; never add game files or client-derived data.
