# Milestone 4 Windows verification handoff

Audience: the person or agent who runs the Windows verification for the Milestone 4 pull request (public API
compatibility and developer reliability). Linux results for everything below are in the pull request description;
this checklist is for the things Linux could not show. It needs no Wizard101 client, and nothing here may add client
files, extracted game data or Imcodec generator inputs to the repository.

## What changed (read first)

* `tools/SpiralLoader.ApiSurface`, `api-baseline/1.2.0/` and `ApiCompatibilityTests`: recorded public API surfaces.
* `SpiralLoader.Host --check`, `--mod`, `--imlight-api`, `--warnings-as-errors`: the developer compatibility check.
* Diagnostics: `fix:` hints, a `Needs attention:` summary, richer messages, codes `assembly.api_newer` and
  `assembly.api_older`.
* `ExternalModCompatibilityTests`, the prebuilt fixture (`tests/SpiralLoader.Tests/Prebuilt`), `examples/standalone-mod`.
* Imlight verify harness: prints and asserts the public `Imlight.CoreLib.Modding` surface; new data mod
  `future-api-verify`.
* Docs: `docs/api-compatibility.md`, `docs/compatibility-testing.md`, `docs/mod-authoring.md`.

The Imlight patch series is **unchanged** (13 commits) and the integration API stays at `1.2.0`.

## 1. Checkout

Use a fresh clone of the pull request branch. Confirm it is based on current `main` (`git log --oneline origin/main..HEAD`
shows only Milestone 4 commits). Check `git config core.autocrlf`: the baseline text files, the `.sh` files (LF) and
the `.ps1` files (CRLF) must survive your checkout settings; report it if they do not.

## 2. SpiralLoader build and tests (PowerShell 7 or Windows PowerShell 5.1)

```powershell
dotnet build SpiralLoader.sln -c Release
dotnet test SpiralLoader.sln -c Release --no-build
```

Expected: 0 warnings, 0 errors; **172 passed**. Two things are Windows-specific risks:

* `ExternalModCompatibilityTests.ModBuiltOutsideTheRepositoryWithThePlainDotnetCliLoadsCleanly` runs `dotnet build`
  on a copy of `examples/standalone-mod` from a temp directory (path quoting, `-o`, locked files).
* `ApiCompatibilityTests` compares text baselines written with LF; they must pass with CRLF checkouts.

If either fails, keep the full output; do not edit baselines to make them pass.

## 3. The developer check, by hand

```powershell
dotnet run --project src/SpiralLoader.Host -c Release -- --check --mods artifacts/mods
dotnet run --project src/SpiralLoader.Host -c Release -- --check --mods artifacts/mods --disable hello-spiral; echo $LASTEXITCODE
```

Expected: first PASS (exit 0); second FAIL with `hello-spiral-addon ... dependency.rejected`, a `fix:` line, exit 1.
Then build `examples/standalone-mod` the way `docs/mod-authoring.md` describes, from a directory outside the repository
(your own temp folder), and run `--check --mod <its output folder> --run "standalone you"`.

## 4. Imlight patch verification (both scripts, both revisions)

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File integration/imlight/verify/Verify-Patch.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File integration/imlight/verify/Verify-Patch.ps1 -Commit 2a16010e59b95cc449d8e21542fbf38440e06bcf
```

Optionally also Git Bash: `integration/imlight/verify/verify-patch.sh` and with `IMLIGHT_COMMIT=2a16010e59b95cc449d8e21542fbf38440e06bcf`.
Expected: exit 0, no `FAIL` line. New `ok` lines to look for: 35 lines starting `Imlight.CoreLib.Modding.` plus
`surface Imlight.CoreLib.Modding integration API 1.2.0` and `VERIFY: imlight-api members=35`; `future-api-verify 1.0.0
[Rejected: dependency.version_mismatch]` in the first run and `future-api-verify 1.0.0 [Disabled]` in the second.

## 5. Full patched Imlight builds (needs your locally available Imcodec generator inputs)

As in `MILESTONE3_LOCAL_TEST_RESULTS.md`: for both revisions, set up an isolated checkout with
`integration/imlight/Setup-Imlight.ps1`, copy your own generator inputs into the isolated clone only, and build Release.
Then check what Linux could not:

1. The real public surface equals the harness-compiled one:
   ```powershell
   dotnet build tools/SpiralLoader.ApiSurface -c Release
   dotnet tools/SpiralLoader.ApiSurface/bin/Release/net10.0/SpiralLoader.ApiSurface.dll check <path to built Imlight.CoreLib.dll> api-baseline/1.2.0/Imlight.CoreLib.Modding.txt --namespace Imlight.CoreLib.Modding --strict
   ```
   Expected: "The API matches". Differences are a finding (the harness compiles the same files against stubs, so
   generated-type names are the likeliest cause); report them, do not change the baseline.
2. Build `examples/standalone-mod` (or the M3 external mod) against the real assemblies and start the isolated Director
   with it enabled: no `assembly.api_*` diagnostics, mod initialized. Then with `DisabledMods` containing it and with
   `[SpiralLoader] Enabled = false`: the Director starts as unmodded Imlight does.
3. Write a manifest with `"depends": {"imlight": "^1.3.0"}` into a copy of the mod folder and start the isolated Director:
   it must stop with a report containing `dependency.version_mismatch` and a `fix:` line (default `Abort` policy);
   with that mod in `DisabledMods` it must start.

## 6. Report

Record, without personal paths, for each step: command, exit code, test counts, the Imlight commits used, SDK version
and Windows version, and anything that differed from "Expected". A real client session is still out of scope.
