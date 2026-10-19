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
The test suite (146 tests) covers versioning, manifests, dependency resolution and ordering, assembly loading
and failure isolation (with mods compiled at test time by Roslyn), events, registries, asset planning,
compatibility pins, the Imlight bootstrap, and an out-of-process run of the headless host.

### Headless host

Boots mods exactly as the Imlight integration does, without Imlight:

```powershell
dotnet run --project src/SpiralLoader.Host -- --mods artifacts/mods --run "hello world" --plan-assets
dotnet run --project src/SpiralLoader.Host -- --help
```

Exit codes: `0` success, `1` a mod failed (or a `--run` command was not found, or assets conflict), `2` bad
arguments.

## Imlight integration

### Verify the patch series (no game data needed)

```bash
integration/imlight/verify/verify-patch.sh          # Linux / macOS / Git Bash
```
```powershell
pwsh integration/imlight/verify/Verify-Patch.ps1     # Windows
```

This clones Imlight at the pinned commit into `external/`, applies `integration/imlight/patches`, compiles the
**real patched** `Program.cs` and new CoreLib files against functional stubs (plus the real `Imlight.Common`,
Akka and SpiralLoader), boots `Program.Main` with `hello-spiral` and a mod that registers a game message
service, and checks the output: loader report through Imlight's logger, lifecycle events, and mod services
merged by the real patched service factories. Set `IMLIGHT_COMMIT` (bash) or `-Commit` (PowerShell) to verify
another Imlight commit; both `b07987168fcf…` and `2a16010e59b9…` pass.

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
git format-patch <pinned-commit>..HEAD -o ../../integration/imlight/patches/
```

Then rerun the verification for the pinned commit and current upstream, and update
`integration/imlight/README.md` if the hook surface changed (bump `ImlightModding.IntegrationApiVersion` on
breaking changes).

## Continuous integration

`.github/workflows/ci.yml` builds and tests on `windows-latest` and `ubuntu-latest` and runs the patch
verification on both.

## Troubleshooting

- **`NU1904` for Akka.Remote 1.5.0** when building against Imlight: upstream `Imlight.Common` pins a version
  with a published advisory. SpiralLoader's harness downgrades this to a warning; fixing it is an upstream
  change.
- **Settings from SpiralLoader leaking into Imlight builds**: checkouts under `external/` are shielded by
  `external/Directory.Build.props`. If you clone Imlight somewhere inside this repository other than
  `external/`, add a similar barrier.
- **`git am` fails on Windows**: make sure the patch files were not converted to CRLF (`.gitattributes`
  marks them `-text`; re-clone if your checkout predates it).
