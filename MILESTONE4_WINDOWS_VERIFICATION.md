# Milestone 4 Windows Verification

## Result

**NOT READY TO MERGE until the verification fixes in the separate integration follow-up are incorporated.**
The assigned PR commit passed the build, test suite, compatibility checks, Imlight patch verifiers, and both real
Imlight API baseline comparisons. This verification found two defects: the API-surface tool could not inspect an
assembly whose package dependencies were not beside it and could crash on incomplete arguments; and `--check` did
not warn users that it runs native mod code. Both are corrected on the integration branch with the changes in this
report.

## Tested revision and environment

- PR branch: `claude/compassionate-bardeen-nm5igl`
- Tested PR commit: `a02a096187e5414f1cde8e945e611d6b870ff8b8`
- Windows 11, build `10.0.22621` (x64)
- .NET SDK `10.0.401`, MSBuild `18.9.11`; .NET runtime `10.0.12`
- PowerShell `7.6.6`; Git for Windows `2.53.0.windows.2`
- Verification used a clean detached checkout of the public PR commit. No pre-history-rewrite commits were added.

## SpiralLoader build, tests, and Windows paths

| Check | Result |
| --- | --- |
| `dotnet build SpiralLoader.sln -c Release` | Pass, 0 warnings, 0 errors |
| `dotnet test SpiralLoader.sln -c Release --no-build` | Pass, 172/172; 0 skipped |
| API baseline tests under CRLF checkout | Pass; `core.autocrlf=true`; baselines are LF in Git and CRLF in the working tree |
| Patch script line endings | Pass; `.ps1` checked out CRLF and `.sh` checked out LF |
| External build with spaces in source and output paths | Pass; plain `dotnet build`, 0 warnings/errors; no `SpiralLoader.Api.dll` copied into the mod package |
| External mod `--check --run "standalone you"` | Pass, exit 0; command returned the expected response |

The required prebuilt `api-tour` fixture passes the complete suite without rebuilding. Two clean rebuilds of its
source with SDK `10.0.401` produced identical DLL hashes. The rebuilt DLL hash differs from the committed DLL; the
committed fixture is stored with its source in the same commit and passes its compatibility tour, but the original
SDK/compiler version was not recorded, so byte-for-byte provenance of that historical binary could not be confirmed.

## Compatibility runner

Manual Windows invocations and the complete test suite agree on the documented exit contract:

| Scenario | Result |
| --- | --- |
| Valid standalone external mod | PASS, exit 0 |
| Malformed manifest | `manifest.invalid_json`, useful syntax guidance, exit 1 |
| Missing dependency | `dependency.missing`, install-or-recommend guidance, exit 1 |
| Incompatible host/API version | `dependency.version_mismatch`, remediation guidance, exit 1 |
| Duplicate identifiers | Both copies rejected as `mod.duplicate_id`, exit 1 |
| Disabled incompatible mod | Marked Disabled, code does not run, PASS, exit 0 |
| Warning without strict warning mode | PASS, exit 0 |
| Same warning with `--warnings-as-errors` | FAIL, exit 1 |
| Prebuilt SpiralLoader API `0.1.0.0` mod | Loaded and its `tour` command passed, exit 0 |
| Invalid host arguments | Usage diagnostic, exit 2 |

The check runs lifecycle events and any requested commands and shuts loaded mods down; it is a managed-code
execution path. After the fix, CLI help and the compatibility and authoring guides prominently say that `--check`
executes native code and is neither a security sandbox nor a passive static analyzer. No untrusted or adversarial
test mod was executed.

## Imlight patches, real builds, and API baseline

Both Windows PowerShell and Git Bash patch verifiers passed on both revisions, run serially to avoid shared build
output locks:

- Pinned: `b07987168fcfa2d123a5d78df90ca0c76405e427`
- Supported upstream: `2a16010e59b95cc449d8e21542fbf38440e06bcf`

The verifier scenarios confirmed the public `ModMessageService` surface, owned versus foreign/unclaimed process
reply routing, rollback, fault isolation, session close, duplicate claims, and the disabled-mod vanilla path. The
`Akka.Remote` `NU1904` advisory warning remained visible; it did not fail the harness.

Both complete patched `Imlight.sln` Release builds succeeded using locally available Imcodec generator inputs copied
only into isolated verification checkouts. The pinned build reported 40 warnings and 0 errors; the upstream build
reported 44 warnings and 0 errors. Warnings include existing package-audit and nullable/compiler warnings. No
generator inputs or game assets were added to this repository.

The actual built `Imlight.CoreLib.dll` on each revision matches
`api-baseline/1.2.0/Imlight.CoreLib.Modding.txt` with `--strict`. The baseline was not edited.

A separate external mod deriving from `ModMessageService` and registering it through the public registry compiled
against the actual patched `Imlight.CoreLib.dll` on the pinned build. The isolated Director loaded and initialized
it, logged one mod initialized and zero failed, and created its Akka system. An incompatible `imlight ^1.3.0` mod
under the default Abort policy exited with failure before actor-system creation and printed
`dependency.version_mismatch` plus a `fix:` hint. With that mod disabled, with the loader disabled, and with no mods,
the loader skipped the mod or reported zero mods and Imlight created the Akka system.

The isolated Director could not reach `ServerStarted`: it then stopped while loading `Root.wad` because this
verification checkout had no configured local patch server or game data. No installed client, development Imlight
checkout, game assets, saves, or private caches were modified. The patch harness independently exercised lifecycle
and message dispatch with the real patched session plumbing and functional generated-type stubs. A real client
session was not part of this verification.

## Defects corrected in the integration follow-up

1. **API-surface dependency resolution and argument handling.** The tool previously threw an unhandled
   `FileNotFoundException` while reflecting the real Imlight CoreLib because `Akka.dll` was not beside the target
   assembly. It now resolves target-local dependencies and package runtime assets from the target `.deps.json`,
   reports invalid or unresolvable assemblies as exit 2, and validates positional arguments before indexing them.
   Rechecked against both real CoreLibs (both match), the SpiralLoader API baseline (matches), and malformed/incomplete
   inputs (actionable error, no process crash).
2. **`--check` trust warning.** CLI help and the compatibility and authoring documentation now state before use that
   the command loads and executes native mod code and is not a sandbox or passive static analyzer.

## Remaining limitations

- Full Director startup through resource loading and `ServerStarted` requires an isolated patch server and matching
  Root.wad/game data. This verification intentionally did not obtain or alter those assets.
- No Wizard101 client session was run.
- The original SDK version for the committed prebuilt fixture is not recorded, so its binary could not be reproduced
  byte-for-byte from the current SDK even though same-SDK rebuilds are deterministic and the committed fixture passes.
