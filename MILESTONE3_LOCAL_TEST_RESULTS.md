# Milestone 3 Windows integration test results

**Integration result: pass for the server-side 1.2.0 `ModMessageService` bridge.** Full patched Release builds, separately compiled external-mod builds, generated message-type checks, actual-assembly synthetic dispatch, and isolated Director startup passed. A complete Wizard101 client gameplay session and client-side round trip were not run because no separate safe client setup was available. The installed client was not launched or modified.

The tested SpiralLoader integration commit was `e35ba3f` with the Windows verification corrections from `e56ecdb`.

## Test environment and scope

The test used a separate integration worktree and isolated Imlight clones under the ignored `external/` directory. The existing Imlight development checkout and installed client were left untouched. Client-derived Imcodec generator inputs were read from an existing local Imlight development checkout and copied into the isolated clones only; they are not part of this repository and must not be committed.

The input set matched client revision `r806919.Wizard_1_610`, including the corresponding object-property JSON type dump and message XML definitions. The exact input data is intentionally not redistributed.

## SpiralLoader build and tests

From the repository root:

```powershell
dotnet build SpiralLoader.sln
dotnet test SpiralLoader.sln --no-build
```

Both passed. Build: 0 warnings, 0 errors. Tests: **153 passed, 0 failed, 0 skipped**.

## Imlight patch verification

All script and revision combinations passed: Windows PowerShell and Bash, against the pinned and audited QA revisions.

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File integration/imlight/verify/Verify-Patch.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File integration/imlight/verify/Verify-Patch.ps1 -Commit 2a16010e59b95cc449d8e21542fbf38440e06bcf
bash integration/imlight/verify/verify-patch.sh
$env:IMLIGHT_COMMIT = '2a16010e59b95cc449d8e21542fbf38440e06bcf'
bash integration/imlight/verify/verify-patch.sh
Remove-Item Env:IMLIGHT_COMMIT
```

| Imlight revision | Commit | PowerShell | Bash |
| --- | --- | --- | --- |
| Pinned | `b07987168fcfa2d123a5d78df90ca0c76405e427` | Pass | Pass |
| Audited QA | `2a16010e59b95cc449d8e21542fbf38440e06bcf` | Pass | Pass |

Synthetic scenarios passed on both revisions: claimed replies route only to the owning mod; foreign and unclaimed replies do not reach mod handlers; throwing handlers and constructors are contained; later messages continue dispatching; termination messages route by owner; session-end callbacks run; duplicate claims roll back; and the disabled-mod run preserves built-in service counts and vanilla message decoding. Revision-specific server-selection veto behavior matched each revision's expected output.

## Full patched Imlight Release builds

The full builds used fresh isolated checkouts with the maintained patch series and the locally available matching Imcodec generator inputs. `$RepoRoot` below means the repository root; generator inputs must be supplied locally and are not included in these commands or the repository.

```powershell
$RepoRoot = (Resolve-Path .).Path
pwsh -NoProfile -ExecutionPolicy Bypass -File "$RepoRoot/integration/imlight/Setup-Imlight.ps1" -Destination "$RepoRoot/external/Imlight-full-b07987168fcf" -Commit b07987168fcfa2d123a5d78df90ca0c76405e427
# Copy the matching local Imcodec GeneratorInput files into this isolated checkout.
dotnet build "$RepoRoot/external/Imlight-full-b07987168fcf/src/Imlight.sln" -c Release

pwsh -NoProfile -ExecutionPolicy Bypass -File "$RepoRoot/integration/imlight/Setup-Imlight.ps1" -Destination "$RepoRoot/external/Imlight-full-2a16010e59b9" -Commit 2a16010e59b95cc449d8e21542fbf38440e06bcf
# Copy the same matching local Imcodec GeneratorInput files into this isolated checkout.
dotnet build "$RepoRoot/external/Imlight-full-2a16010e59b9/src/Imlight.sln" -c Release
```

Both complete solution builds passed, including `Imlight.CoreLib` and `Imlight.Director`, against real generated Imcodec types. The pinned build had 42 warnings and 0 errors; the QA build had 44 warnings and 0 errors. Warnings included upstream package-audit, generated nullable/hiding, and existing Imlight warnings. Akka.Remote audit warnings were retained and are documented in [the dependency audit](docs/audit.md).

## Generated process-message types and external mod

The separate `ClientProcessMod` project was built against each real Director output, not the verification harness:

```powershell
$RepoRoot = (Resolve-Path .).Path
$PinnedDirector = "$RepoRoot/external/Imlight-full-b07987168fcf/src/Imlight.Director/bin/Release/net10.0"
$QaDirector = "$RepoRoot/external/Imlight-full-2a16010e59b9/src/Imlight.Director/bin/Release/net10.0"
dotnet build "$RepoRoot/integration/imlight/verify/ClientProcessMod/ClientProcessMod.csproj" -c Release "-p:ImlightDirectory=$PinnedDirector"
dotnet build "$RepoRoot/integration/imlight/verify/ClientProcessMod/ClientProcessMod.csproj" -c Release "-p:ImlightDirectory=$QaDirector"
```

Both passed with 0 warnings and 0 errors. A reproducible project defect was fixed: `ClientProcessMod.csproj` had referenced the verification harness unconditionally. Its harness reference is now used only when `ImlightDirectory` is unset.

Temporary local probes, kept only under ignored `external/`, inspected each real `Imcodec.MessageLayer.dll`. They reported:

- `MSG_MESSAGE_PROCESS.JobID` and `MSG_CLIENT_PROCESS_TERMINATED.JobID` are writable `System.UInt32` fields.
- `MSG_MESSAGE_PROCESS.Message` and `MSG_START_CLIENT_PROCESS.ScriptName` are writable `Imcodec.IO.ByteString` fields with working string conversions.
- `MSG_START_CLIENT_PROCESS.JobID` and `MSG_KILL_CLIENT_PROCESS.JobID` are writable `System.UInt32` fields.
- `MSG_MESSAGE_PROCESS`, `MSG_CLIENT_PROCESS_TERMINATED`, `MSG_START_CLIENT_PROCESS`, and `MSG_KILL_CLIENT_PROCESS` implement `Imcodec.MessageLayer.IMessage`.

The temporary probe sources and generated inputs are not distributed. The maintained verification harness and source patch series are included; the full-build and probe results above record the additional local integration evidence.

## Synthetic dispatch against actual built CoreLib assemblies

A temporary local dispatch probe referenced the actual patched CoreLib, Akka, Imlight.Common, and generated Imcodec assemblies for each revision. It created a real Akka `SessionActor`, loaded the registered internal game message-service host, and sent generated process messages through the real `MSG_RECEIVEDPACKET` path.

Both revisions printed `REAL_ASSEMBLY_DISPATCH_PASS`. Assertions confirmed that distinct owners received only their own claimed job replies, an unclaimed ID reached neither mod, an intentional handler exception was logged and isolated, a later message still dispatched, and every service's `OnSessionEnded` ran exactly once when its real session disposed. Expected Akka dead-letter output appeared as the synthetic socket pair shut down; it did not affect assertions. Probe sources were temporary and are not part of the repository.

## Isolated Director startup and vanilla behavior

The pinned Release output was run as an isolated server with private ports, databases, mod data, cache path, and logs under its own build output directory. A separately compiled external `client-process-verify` mod was installed there.

```powershell
$RepoRoot = (Resolve-Path .).Path
$PinnedDirector = "$RepoRoot/external/Imlight-full-b07987168fcf/src/Imlight.Director/bin/Release/net10.0"
Set-Location $PinnedDirector
dotnet Imlight.Director.dll
```

With `[SpiralLoader] Enabled = true`, the Director found and initialized the external mod, created its login and game `ModMessageServiceHost` instances, bound its isolated listeners, loaded cached resources, and reached `Imlight may now be connected to.` An initial attempt with an empty isolated WAD cache stopped because no local `Root` definition was present. Recovery copied the existing cache into the isolated server only; the original was not modified. The separate patch endpoint was unavailable, but the server continued using cached resources.

The pinned Director was then launched with `[SpiralLoader] Enabled = false`. It reported `Imlight runs without mods`, skipped loader startup, loaded the same isolated resources, bound its listeners, and reached the ready message. No installed Wizard101 client was launched.

## Repository corrections

- `ClientProcessMod.csproj`: made the harness project reference conditional, allowing separate compilation against real Imlight assemblies.
- `integration/imlight/README.md`: documented the full Release builds, real generated message types, real-assembly synthetic dispatch, and the pinned-only Director launches.
- `docs/building.md`: corrected the automated test count from 150 to 153.
- `docs/next-milestone.md`: reflects that local full-build and external bridge verification is complete, while a real client session remains open.

No reproducible build, registration, routing, ownership-isolation, exception-containment, or session-disposal defect remains in the 1.2.0 server-side bridge. A real client Lua round trip at character select or in game remains unverified because a separate safe client setup was unavailable.
