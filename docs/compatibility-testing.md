# Testing your mod's compatibility

You can check that a mod loads, initializes, shuts down and reports problems properly **without Wizard101 files,
without an Imlight checkout and without a game client**, using the headless host:

> **Security:** `--check` loads assemblies and executes mod code, including constructors, `Initialize`, lifecycle
> handlers, requested `--run` commands, and `Shutdown`. It is **not a security sandbox** and it is **not a passive
> static analyzer**. A mod can access files, processes, and the network with the permissions of the host process.
> Only run it on mods you trust. Use an operating-system isolation boundary if you must inspect an untrusted mod.

```bash
# once: build the host and the API your mod references
dotnet build src/SpiralLoader.Host -c Release
dotnet build src/SpiralLoader.Api -c Release

# build your mod into a folder (see mod-authoring.md), then:
dotnet src/SpiralLoader.Host/bin/Release/net10.0/SpiralLoader.Host.dll --check --mod path/to/my-mod
```

`--check` does what a server start does — discover, parse, filter, resolve, load, initialize, freeze — but keeps going
past the first failure, publishes the server lifecycle events so your handlers run, shuts the mods down, and ends with
a verdict and an exit code (`0` pass, `1` fail, `2` bad arguments):

```
SpiralLoader compatibility check: FAIL
  host: SpiralLoader.Host; spiralloader 0.1.0; no imlight built-in (pass --imlight-api <version> to simulate it)
  my-mod 1.0.0: Rejected (dependency.missing)
  Not covered: behaviour that needs a running Imlight server or a game client. ...
```

Above the verdict the full load report lists every mod with its state, each diagnostic with its stable code and
message, and a `fix:` line with what to do about it. The `Needs attention:` line names each failed mod and the codes
to look up in the [diagnostics table](architecture.md#diagnostics).

| Option | Meaning |
| --- | --- |
| `--mod <dir>` | One mod folder (contains `spiralmod.json`); repeatable. Typically your build output |
| `--mods <dir>` | A directory that **contains** mod folders (what a server's `mods/` is); repeatable |
| `--imlight-api <version>` | Provide the `imlight` built-in at this integration API version, so `"depends": {"imlight": "^1.2.0"}` resolves. Simulated: no Imlight code runs |
| `--disable <id>` | Treat a mod as disabled, as `DisabledMods` does on a server |
| `--run "<command>"` | Run a command registered through `CommonRegistries.Commands` after startup (a cheap way to assert behaviour) |
| `--warnings-as-errors` | Warning diagnostics (for example `recommends.missing`, `assembly.api_older`) fail the check |
| `--imlight-commit <sha>`, `--client-revision <r>`, `--compat strict\|warn` | Test your `compatibility` pins |
| `--verbose`, `--data`, `--config`, `--profile` | As for the headless host |

Run it for **every combination you claim to support**: with `--imlight-api` at your minimum version, at the newest one,
and once without (if your mod only needs `spiralloader`). A mod that declares `"imlight": "^1.2.0"` must pass at `1.2.0`
and be rejected cleanly (`dependency.version_mismatch`) at `1.1.0`.

In CI (GitHub Actions) for a mod repository, after checking out SpiralLoader next to it:

```yaml
- run: dotnet build SpiralLoader/src/SpiralLoader.Host -c Release
- run: dotnet build SpiralLoader/src/SpiralLoader.Api -c Release
- run: dotnet build MyMod -c Release -o mods/my-mod -p:SpiralLoaderApiPath=${{ github.workspace }}/SpiralLoader/src/SpiralLoader.Api/bin/Release/net10.0/SpiralLoader.Api.dll
- run: dotnet SpiralLoader/src/SpiralLoader.Host/bin/Release/net10.0/SpiralLoader.Host.dll --check --mods mods --imlight-api 1.2.0 --warnings-as-errors
```

## What it proves, and what it does not

It proves the mod **loads and initializes against the SpiralLoader runtime**: manifest valid, dependencies and ranges
satisfied, assemblies resolve, entrypoints construct, `Initialize` and `Shutdown` run, registrations are accepted,
handlers for the lifecycle events do not throw, commands behave.

It does **not** prove anything that needs Imlight or a client: Imlight hooks (`LootGrantingEvent`,
`ModMessageService` handlers, ...) are never raised by the headless host; `Imlight.CoreLib` types referenced by your
mod must still be resolvable, which only a build against a real patched Imlight shows (`ImlightDirectory` in
[building.md](building.md)); client Lua/WAD behaviour is not exercised at all. The Imlight patch verification harness
covers the hook side on stubs (below). A real client session remains unverified.

## Scenario catalogue

Each behaviour third-party mods meet, with the diagnostic code a developer sees and the test that pins it. The suites
overlap deliberately little: scenarios the older suites already covered point at them; `ExternalModCompatibilityTests`
and `ApiCompatibilityTests` hold only what was missing. `ExternalModCompatibilityTests.EveryTestNamedInTheCompatibilityGuideExists`
fails if a test named here is renamed or removed.

| Scenario | Expected result | Covered by |
| --- | --- | --- |
| Correctly packaged external C# mod, built outside the repository with the plain `dotnet` CLI | Loads, no diagnostics, command works, no `SpiralLoader.Api.dll` in the package, `--check` passes | `ExternalModCompatibilityTests.ModBuiltOutsideTheRepositoryWithThePlainDotnetCliLoadsCleanly`, `ExampleModIntegrationTests.LoadsExampleModsInProcess`, `ExampleModIntegrationTests.HeadlessHostProcessLoadsExampleMods` |
| Previously compiled mod, current runtime, no rebuild | The committed `api-tour` binary (built against API assembly `0.1.0.0`) loads, runs every API area, passes its own checks | `ExternalModCompatibilityTests.ModCompiledAgainstTheBaselineApiLoadsAndPassesItsTourWithoutRebuilding` |
| Mod ships its own copy of the API | Host copy wins, `assembly.shared_copy` warning | `LoadingTests.ModShippingItsOwnApiCopyStillBindsToHostApi` |
| Mod built against an older API assembly version | Loads, `assembly.api_older` warning | `ExternalModCompatibilityTests.ModBuiltAgainstAnOlderApiStillRunsAndIsToldItMayNeedARebuild` |
| Mod built against a newer API assembly version | Failed before its code runs, `assembly.api_newer` | `ExternalModCompatibilityTests.ModBuiltAgainstANewerApiIsRefusedBeforeItsCodeRuns` |
| Missing required dependency | Rejected, `dependency.missing`; dependents rejected with `dependency.rejected` | `ResolutionTests.RejectsMissingDependency`, `ResolutionTests.RejectionCascadesToDependents` |
| Incompatible dependency version | Rejected, `dependency.version_mismatch` (a mod), or `breaks.present` | `ResolutionTests.RejectsDependencyVersionMismatch`, `ResolutionTests.RejectsModThatBreaksAnInstalledMod` |
| Duplicate mod ids | Every copy rejected, `mod.duplicate_id` names the folders; reserved ids `mod.reserved_id` | `ResolutionTests.RejectsDuplicateIds`, `ResolutionTests.RejectsReservedBuiltInId` |
| Unsupported manifest schema, malformed manifest | Rejected, `manifest.unsupported_schema` / `manifest.invalid_field` / `manifest.invalid_json`; every problem reported at once | `ManifestTests.RejectsFutureSchemaVersion`, `ManifestTests.RejectsInvalidManifests`, `ManifestTests.ReportsEveryErrorAtOnce` |
| Missing, corrupt or unresolvable assembly | Rejected `assembly.missing`, or failed `assembly.load_failed`; other mods unaffected | `ResolutionTests.RejectsMissingDeclaredAssembly`, `LoadingTests.CorruptAssemblyFailsOnlyThatMod`, `LoadingTests.UnresolvableReferenceFailsAtLoadNotLater` |
| Invalid entrypoint (not found, abstract, no parameterless constructor, not `ISpiralMod`) | Failed, `entrypoint.not_found` (lists the real `ISpiralMod` types) / `entrypoint.invalid` | `LoadingTests.RejectsInvalidEntrypoints`, `ExternalModCompatibilityTests.EveryFailureNamesTheModTheCodeTheCauseAndWhatToDo` |
| `Initialize` or the constructor throws | Failed, `init.failed` with the cause; every subscription and registration rolled back; dependents `dependency.failed` | `LoadingTests.InitializationFailureRollsBackRegistrationsAndFailsDependents`, `LoadingTests.ConstructorFailureIsReported` |
| Conflicting registry contributions | The second mod's `Initialize` throws (`init.failed` naming the holder) and is rolled back; the first keeps its entry | `LoadingTests.DuplicateClaimInUniqueRegistryFailsOnlyTheSecondMod`, `EventAndRegistryTests.UniqueValueRegistriesRejectSecondClaimAcrossMods`, `EventAndRegistryTests.RegistryNamespacesEntriesAndKeepsOrder`, `EventAndRegistryTests.CommandRouterPrefersEarlierModAndValidatesNames`; real Imlight: `duplicate-claim-verify` in the harness |
| Incompatible host versions | `spiralloader`, `dotnet`, `imlight` ranges checked like mods (`dependency.version_mismatch`); Imlight commit / client revision pins `compat.*` (reject in Strict, warn in WarnOnly) | `ResolutionTests.ChecksDependenciesOnHostBuiltIns`, `ImlightBootstrapTests.ModsCanRequireHooksAddedInMinorIntegrationVersions`, `AssetAndCompatibilityTests.StrictModeRejectsCommitMismatch`, `AssetAndCompatibilityTests.StrictModeRejectsClientRevisionMismatch`, `AssetAndCompatibilityTests.WarnOnlyModeLoadsWithWarning`; real Director: `future-api-verify` in the harness |
| Disabling an incompatible mod | Server starts without it; dependents must be disabled too (otherwise `dependency.rejected`); the disabled mod's code never ran | `ExternalModCompatibilityTests.DisablingAnIncompatibleModLetsTheServerStartWithoutItOrItsDependents`, `ResolutionTests.DisabledModsAreSkippedAndTheirDependentsRejected`, `ExternalModCompatibilityTests.CheckCommandReportsFailuresWithCodesAndFixesAndDisablingMakesItPass`; real Director: the harness's vanilla run disables `future-api-verify` under the `Abort` policy |
| Failure reporting | Mod, code, cause and a `fix:` line in the report and in the startup-abort exception | `ExternalModCompatibilityTests.EveryFailureNamesTheModTheCodeTheCauseAndWhatToDo`, `ExternalModCompatibilityTests.TheStartupAbortCarriesTheSameActionableReport`, `ResolutionTests.AbortPolicyThrowsWithFullReport` |
| The `--check` command | Exit codes, verdict, simulated `imlight` built-in, `--mod` / `--disable` / `--warnings-as-errors` | `ExternalModCompatibilityTests.CheckCommandReportsFailuresWithCodesAndFixesAndDisablingMakesItPass`, `ExternalModCompatibilityTests.CheckCommandExplainsAFolderThatIsNotAModAndAnEmptyModsDirectory`, `ExternalModCompatibilityTests.CheckCommandCanTreatWarningsAsFailures` |
| Public API and diagnostic code regressions | Removed or changed members fail; additions require a baseline update; every code is documented and has a hint | `ApiCompatibilityTests.ApiSurfaceMatchesTheNewestBaselineExactly`, `ApiCompatibilityTests.ApiSurfaceKeepsEverythingEveryRecordedBaselineHad`, `ApiCompatibilityTests.DiagnosticCodesNeverDisappearOrChange`, `ApiCompatibilityTests.EveryDiagnosticCodeIsDocumentedAndEveryProblemCodeHasAdvice`, `ApiCompatibilityTests.SurfaceDiffNoticesRemovedChangedAndAddedMembers` |

### Scenarios that need Imlight

Anything involving Imlight's own types goes through the existing Imlight verification harness
(`integration/imlight/verify`, see [integration/imlight/README.md](../integration/imlight/README.md)), on both
supported revisions: it compiles the real patched Director and session plumbing against stubs, boots `Program.Main`,
and asserts the output. Compatibility lines it asserts: the integration API a mod can see is `1.2.0`; a data-only mod
requiring `^1.3.0` is rejected with `dependency.version_mismatch` and doesn't affect the others; with that mod
disabled the Director starts under the `Abort` policy; the public surface of `Imlight.CoreLib.Modding` equals
`api-baseline/1.2.0/Imlight.CoreLib.Modding.txt`.

## The prebuilt fixture

`tests/SpiralLoader.Tests/Prebuilt/api-0.1.0/api-tour/` is a **committed binary**: a mod compiled once against
`SpiralLoader.Api` assembly version `0.1.0.0`. The test suite never rebuilds it, so it stays the stand-in for "a mod
somebody compiled earlier". It touches the manifest, environment, logger, mod query, events (priority, cancellation,
monitor, lifecycle, a mod-defined `CancellableEvent`), registries (a host registry, a missing one, name rules),
version ranges, the null implementations and the freeze behaviour, and reports the result through its `tour`
command. If a change to the runtime or API breaks binary compatibility, this test fails with the check that broke.

Its source is `tests/fixtures/ApiTourMod` (not in the solution). Refresh it only when you intend to move the
compatibility target, for example after a breaking change that gets a new assembly version, in a new
`Prebuilt/api-<version>/` folder so the old binary keeps running:

```bash
dotnet build tests/fixtures/ApiTourMod -c Release        # writes tests/SpiralLoader.Tests/Prebuilt/api-0.1.0/api-tour
```

The build is deterministic (no symbols, mapped source paths), so a rebuild with the same SDK yields the same bytes.
The fixture contains no game data, only original code under the repository's license.

## Running the suites

```bash
dotnet test SpiralLoader.sln                                   # everything
dotnet test SpiralLoader.sln --filter FullyQualifiedName~ExternalModCompatibilityTests
dotnet test SpiralLoader.sln --filter "Category!=ExternalBuild"   # skip the test that runs `dotnet build` on the template
integration/imlight/verify/verify-patch.sh                       # Imlight side, pinned revision
IMLIGHT_COMMIT=2a16010e59b95cc449d8e21542fbf38440e06bcf integration/imlight/verify/verify-patch.sh
```
