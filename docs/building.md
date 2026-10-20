# Building and testing

## Prerequisites

- **.NET 10 SDK** (`global.json` pins `10.0.100` with `rollForward: latestFeature`).
- **Git**.
- Windows is the primary platform; everything below except the full Imlight build also runs on Linux and macOS.
- PowerShell 7 (`pwsh`) is recommended on Windows; the `.ps1` scripts avoid PowerShell-7-only APIs, but they
  are tested under `pwsh`.

## SpiralLoader itself (no game data needed)

```powershell
dotnet build SpiralLoader.sln
dotnet test SpiralLoader.sln
```

`dotnet build` also assembles the example mods into installable folders under `artifacts/mods/`.
The test suite (172 tests) covers versioning, manifests, dependency resolution and ordering, assembly loading
and failure isolation (with mods compiled at test time by Roslyn), events, registries, asset planning,
compatibility pins, the Imlight bootstrap, an out-of-process run of the headless host, the public API baselines
(`ApiCompatibilityTests`) and external-mod compatibility (`ExternalModCompatibilityTests`: a mod built outside the
repository with the plain `dotnet` CLI, a committed prebuilt binary, API-version mismatches, disabling an incompatible
mod, the `--check` command, diagnostics quality). See [compatibility-testing.md](compatibility-testing.md). One test runs
`dotnet build` on `examples/standalone-mod`; skip it with `--filter "Category!=ExternalBuild"`.

### Headless host

Boots mods exactly as the Imlight integration does, without Imlight:

```powershell
dotnet run --project src/SpiralLoader.Host -- --mods artifacts/mods --run "hello world" --plan-assets
dotnet run --project src/SpiralLoader.Host -- --help
```

Exit codes: `0` success, `1` a mod failed (or a `--run` command was not found, or assets conflict), `2` bad
arguments.

### Checking a mod (no game data)

```powershell
dotnet run --project src/SpiralLoader.Host -c Release -- --check --mod path\to\my-mod --imlight-api 1.2.0
```

Loads without aborting at the first failure, runs the lifecycle events, prints a PASS/FAIL verdict with each failure's
diagnostic code and a `fix:` line. Options and CI usage: [compatibility-testing.md](compatibility-testing.md).

### Public API baselines

`tools/SpiralLoader.ApiSurface` prints and compares public API surfaces against `api-baseline/`:

```powershell
dotnet build src/SpiralLoader.Api -c Release; dotnet build tools/SpiralLoader.ApiSurface -c Release
dotnet tools/SpiralLoader.ApiSurface/bin/Release/net10.0/SpiralLoader.ApiSurface.dll check src/SpiralLoader.Api/bin/Release/net10.0/SpiralLoader.Api.dll api-baseline/1.2.0/SpiralLoader.Api.txt --strict
```

See [api-compatibility.md](api-compatibility.md) for what the baselines mean and how to update them.

## Imlight integration

### Verify the patch series (no game data needed)

```bash
integration/imlight/verify/verify-patch.sh          # Linux / macOS / Git Bash
```
```powershell
pwsh integration/imlight/verify/Verify-Patch.ps1     # Windows
```

This clones Imlight at the pinned commit into `external/`, applies `integration/imlight/patches`, compiles the
**real patched** `Program.cs`, the modding files, Imlight's real session plumbing (`SessionActor`,
`MessageService`, `ReceiveProtocolDispatcher`, `MessageHandlerTable`) and the hook call-site services
(`CharacterService`, `GameTransitionService`, `MinigameService`, `ProcessSupervisor`) against functional stubs
(plus the real `Imlight.Common`, Akka and SpiralLoader). It builds `hello-spiral` and four separately compiled
verification mods that see only the public surface, then boots `Program.Main` twice:

1. **With every mod** (`FailurePolicy = Continue`, so the deliberately failing `duplicate-claim-verify` is reported
   rather than aborting). `verify/Scenarios.cs` creates real `SessionActor`s from the real patched service
   factories and injects synthetic client packets exactly as `SocketListener` does (`MSG_RECEIVEDPACKET`): the
   character list, the client's `MSG_MESSAGE_PROCESS` reply to the menu process a mod started from
   `CharacterListSentEvent`, game server selection, and a game session with replies for owned, foreign, rolled-back
   and unclaimed job ids, a faulting handler and a process termination. Asserted: `verify/expected-output.txt`, plus
   `verify/expected-output-<commit12>.txt` for revision-specific behaviour (whether a rejected game server selection
   closes the session).
2. **With the client-process mods disabled** (`DisabledMods`), asserting `verify/expected-vanilla-output.txt`:
   no mod service host, no claims, and every client process reply decoded by `MinigameService` as unmodded Imlight
   does.

Expectation lines starting with `!` must not appear. Set `IMLIGHT_COMMIT` (bash) or `-Commit` (PowerShell) to
verify another Imlight commit; both `b07987168fcf…` and `2a16010e59b9…` pass. Imcodec's generated types are
stubbed in `verify/ImcodecStubs.cs`, and the `ZONE_102`/`CHARACTER_103` packet classes (which need dozens of
generated zone types) in `verify/Stubs.cs`; those stubs follow how the pinned source uses the types and cannot
prove they match the generated code.

What it does **not** prove: that all of the patched `Imlight.CoreLib` compiles. The `LootGranter` and
`CommandDispatcher` call sites, and everything else that needs Imcodec's generated types, are only compiled by
a full build.

### Full Imlight build with mods (local machine with game data)

Imlight's Imcodec submodule generates its wire and template types from client-derived inputs that are **not
distributed** (Imcodec's `!base` placeholders): a WizWalker type dump (`*.json`) in
`submodule/Imcodec/src/Imcodec.ObjectProperty/GeneratorInput/` and message definitions (`*.xml`) in
`submodule/Imcodec/src/Imcodec.MessageLayer/GeneratorInput/`, matching your client revision. Obtain them as you
would for a normal Imlight build.

```powershell
.\integration\imlight\Setup-Imlight.ps1                  # clone + submodules + patches -> external\Imlight
# copy your Imcodec generator inputs into external\Imlight\submodule\Imcodec\src\...\GeneratorInput\
dotnet build external\Imlight\src\Imlight.sln -c Release # SpiralLoaderRoot is detected (Imlight inside external\)
```

If Imlight lives elsewhere, pass `-p:SpiralLoaderRoot=C:\path\to\SpiralLoader\` to the build.

Run it with mods:

1. Create `mods\` next to `Imlight.Director.exe` (in `src\Imlight.Director\bin\Release\net10.0\`).
2. Copy a mod folder into it, for example `artifacts\mods\hello-spiral`.
3. Start `Imlight.Director.exe`. The log shows the SpiralLoader report before the Akka system starts, and
   `[hello-spiral] Server started; wrote …` after "Imlight may now be connected to."
4. In game, an account with `AuthLevel` above `None` can type `.hello` in chat (Imlight's command prefix is `.`).

Configuration lives in the `[SpiralLoader]` section of `Config/Imlight.ini` (added by the patch; every key
optional). With `Enabled = false`, or with no mods installed, Imlight behaves as it does unpatched.

### Updating the patch series

```bash
cd external/Imlight          # a checkout produced by setup-imlight.sh, on branch "spiralloader"
# edit, then amend or add commits on top of the pinned commit
../../integration/imlight/regen-patches.sh     # deletes the old series, then git format-patch <pinned>..HEAD
```

Then rerun the verification for the pinned commit and current upstream, and update
`integration/imlight/README.md` if the hook surface changed (bump `ImlightModding.IntegrationApiVersion`: minor for
additions, major for breaks) and record the new `Imlight.CoreLib.Modding` surface under `api-baseline/`
([api-compatibility.md](api-compatibility.md#checking-and-updating-a-baseline)). The verify scripts fail until you do.

## Continuous integration

`.github/workflows/ci.yml` builds and tests on `windows-latest` and `ubuntu-latest` and runs the patch
verification on both.

## Troubleshooting

- **`NU1904` for Akka.Remote** when building against Imlight: the upstream versions in the supported revisions
  are below 1.5.52 and fall within critical advisory GHSA-jhpv-4q4f-43g5 / CVE-2025-61778. The harness's
  separately compiled verification-mod projects mark NU1904 as not-an-error so the tests can finish; the warning
  remains visible and is not suppressed. Full patched Imlight builds also reported package-audit warnings. See
  [docs/audit.md](audit.md) for the affected TLS certificate-authentication scenario and upstream fix.
- **Settings from SpiralLoader leaking into Imlight builds**: checkouts under `external/` are shielded by
  `external/Directory.Build.props`. If you clone Imlight somewhere inside this repository other than
  `external/`, add a similar barrier.
- **`git am` fails on Windows**: make sure the patch files were not converted to CRLF (`.gitattributes`
  marks them `-text`; re-clone if your checkout predates it).
