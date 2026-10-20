# Milestone 3 Windows integration test results

**Result: READY TO MERGE** for the server-side 1.2.0 `ModMessageService` bridge. The patched Release builds, the separately compiled external mod, service creation in a real Director, and synthetic dispatch through each actual built CoreLib all passed. A Wizard101 client round-trip was not run because no separate safe client setup was available; the installed client was not modified or launched.

## Checkout and preservation

- Repository: `https://github.com/ToontownSuperForeverMVP/SpiralLoader`
- PR ref: `refs/pull/3/head`, commit `e35ba3f` (`feat: public ModMessageService bridge for client process replies`)
- Local branch: `integration/milestone3-windows`
- Integration worktree: `C:\Users\Shadow\Desktop\SL\SL-milestone3`
- The existing `C:\Users\Shadow\Desktop\SL` checkout, its dirty Milestone 2 files, both Claude branches, `C:\Users\Shadow\Desktop\imlight\Imlight`, and the installed Wizard101 client/saves were left untouched. Imlight test clones, build output, copied cache, test databases, and logs are under this worktree's ignored `external/` directory.

The requested project instructions were read: `CLAUDE.md`, `docs/building.md`, `integration/imlight/README.md`, and `docs/next-milestone.md`.

## SpiralLoader build and tests

Command:

```powershell
dotnet build SpiralLoader.sln
```

Result: passed, 0 warnings, 0 errors. The example mod folders were assembled under `artifacts/mods/`.

Command:

```powershell
dotnet test SpiralLoader.sln --no-build
```

Result: **153 passed, 0 failed, 0 skipped**.

## Imlight patch verification

All four script/revision combinations passed: both the Windows PowerShell script and the Bash twin, against the pinned and audited QA revisions.

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File integration/imlight/verify/Verify-Patch.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File integration/imlight/verify/Verify-Patch.ps1 -Commit 2a16010e59b95cc449d8e21542fbf38440e06bcf
& 'C:\Program Files\Git\bin\bash.exe' integration/imlight/verify/verify-patch.sh
$env:IMLIGHT_COMMIT='2a16010e59b95cc449d8e21542fbf38440e06bcf'; & 'C:\Program Files\Git\bin\bash.exe' integration/imlight/verify/verify-patch.sh; $result=$LASTEXITCODE; Remove-Item Env:IMLIGHT_COMMIT; exit $result
```

| Imlight revision | Commit | PowerShell | Bash |
| --- | --- | --- | --- |
| Pinned | `b07987168fcfa2d123a5d78df90ca0c76405e427` | Pass | Pass |
| Audited QA | `2a16010e59b95cc449d8e21542fbf38440e06bcf` | Pass | Pass |

The synthetic scenarios passed for both revisions: claimed replies route only to the owning mod, foreign and unclaimed replies stay out of mod handlers, a throwing handler does not stop later dispatch, constructor failures are contained, termination messages route by owner, session-end callbacks run, duplicate job claims roll back, and the client-process mods disabled run keeps built-in service counts and vanilla message decoding. The revision-specific veto behavior matched each expectation file.

The harness build reports upstream `NU1904` warnings for Akka.Remote 1.5.0. These did not fail the builds and were not suppressed for the full patched server builds.

## Full patched Release builds with real generator inputs

The existing Imlight development checkout already contained the client-derived inputs for client revision `r806919.Wizard_1_610`:

- `submodule/Imcodec/src/Imcodec.ObjectProperty/GeneratorInput/r806919_Wizard_1_610.json`
- `submodule/Imcodec/src/Imcodec.MessageLayer/GeneratorInput/*.xml`

Fresh isolated Imlight clones were made and patched using `Setup-Imlight.ps1`; only those generator-input directories were copied into the clones. The source checkout was read only.

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File integration/imlight/Setup-Imlight.ps1 -Destination external/Imlight-full-b07987168fcf -Commit b07987168fcfa2d123a5d78df90ca0c76405e427
# Copy the two GeneratorInput directory contents from the existing Imlight checkout into the corresponding clone paths.
dotnet build external/Imlight-full-b07987168fcf/src/Imlight.sln -c Release

pwsh -NoProfile -ExecutionPolicy Bypass -File integration/imlight/Setup-Imlight.ps1 -Destination external/Imlight-full-2a16010e59b9 -Commit 2a16010e59b95cc449d8e21542fbf38440e06bcf
# Copy the same two GeneratorInput directory contents into this clone.
dotnet build external/Imlight-full-2a16010e59b9/src/Imlight.sln -c Release
```

Both complete solution builds passed, including `Imlight.CoreLib` and `Imlight.Director` with the PR patch series and real generated Imcodec types. Pinned: 42 warnings, 0 errors. QA: 44 warnings, 0 errors. Warnings are upstream package audit, Imcodec-generated nullable/hiding warnings, and existing Imlight warnings; neither build had a compile error.

## Real generated process message types

The external `ClientProcessMod` was built against each real Director output, not the patch-verification harness:

```powershell
dotnet build integration/imlight/verify/ClientProcessMod/ClientProcessMod.csproj -c Release -p:ImlightDirectory=C:\Users\Shadow\Desktop\SL\SL-milestone3\external\Imlight-full-b07987168fcf\src\Imlight.Director\bin\Release\net10.0
dotnet build integration/imlight/verify/ClientProcessMod/ClientProcessMod.csproj -c Release -p:ImlightDirectory=C:\Users\Shadow\Desktop\SL\SL-milestone3\external\Imlight-full-2a16010e59b9\src\Imlight.Director\bin\Release\net10.0
```

Both passed with 0 warnings and 0 errors. The project previously referenced `ImlightPatchVerify.csproj` unconditionally, mixing harness and real-server references. I fixed that reproducible test-project defect in `integration/imlight/verify/ClientProcessMod/ClientProcessMod.csproj`: the harness reference is now used only when `ImlightDirectory` is unset.

A small probe compiled and ran against each actual `Imcodec.MessageLayer.dll`:

```powershell
dotnet run --project external/GeneratedTypeProbe/GeneratedTypeProbe.csproj -p:AssemblyDirectory=C:\Users\Shadow\Desktop\SL\SL-milestone3\external\Imlight-full-b07987168fcf\src\Imlight.Director\bin\Release\net10.0
dotnet run --project external/GeneratedTypeProbe/GeneratedTypeProbe.csproj -p:AssemblyDirectory=C:\Users\Shadow\Desktop\SL\SL-milestone3\external\Imlight-full-2a16010e59b9\src\Imlight.Director\bin\Release\net10.0
```

Both revisions reported the same real generated API:

- `MSG_MESSAGE_PROCESS.JobID` and `MSG_CLIENT_PROCESS_TERMINATED.JobID`: writable `System.UInt32`.
- `MSG_MESSAGE_PROCESS.Message` and `MSG_START_CLIENT_PROCESS.ScriptName`: writable `Imcodec.IO.ByteString`; assigning, comparing, and displaying string values works through the generated type's conversions.
- `MSG_START_CLIENT_PROCESS.JobID` and `MSG_KILL_CLIENT_PROCESS.JobID`: writable `System.UInt32`.
- All four messages implement `Imcodec.MessageLayer.IMessage`.

This corrects an outdated README assumption that `Message` is a `string`; the integration compiles and works against the generated `ByteString` API.

## Synthetic dispatch against the actual built CoreLibs

To avoid treating the stub harness as binary-level proof, `external/RealDispatchProbe` references the actual patched CoreLib, Akka, Imlight.Common, and generated Imcodec assemblies. It creates a real Akka `SessionActor`, loads the actual internal `GameModMessageServiceHost` by its registered runtime type, and injects real generated `MSG_MESSAGE_PROCESS` packets through the real `MSG_RECEIVEDPACKET` path.

```powershell
dotnet run --project external/RealDispatchProbe/RealDispatchProbe.csproj -p:ImlightDirectory=C:\Users\Shadow\Desktop\SL\SL-milestone3\external\Imlight-full-b07987168fcf\src\Imlight.Director\bin\Release\net10.0 -- C:\Users\Shadow\Desktop\SL\SL-milestone3\external\Imlight-full-b07987168fcf\src\Imlight.Director\bin\Release\net10.0\Config\Imlight.ini
dotnet run --project external/RealDispatchProbe/RealDispatchProbe.csproj -p:ImlightDirectory=C:\Users\Shadow\Desktop\SL\SL-milestone3\external\Imlight-full-2a16010e59b9\src\Imlight.Director\bin\Release\net10.0 -- C:\Users\Shadow\Desktop\SL\SL-milestone3\external\Imlight-full-2a16010e59b9\src\Imlight.Director\bin\Release\net10.0\Config\Imlight.ini
```

Both printed `REAL_ASSEMBLY_DISPATCH_PASS`. Assertions confirmed that job 770101 reached only owner A, 770201 reached only owner B, unclaimed job 424242 reached neither, owner A's intentional handler exception was logged and isolated, a later owner-A message still dispatched, and each service's `OnSessionEnded` ran exactly once when the real session disposed. The harness produced expected Akka dead-letter noise while the synthetic socket pair was being torn down; it did not affect the assertions.

## Isolated Director launches

The pinned Release output was used as the isolated server. Its config used login port 25200, game ports 25233–25235, patch port 25250, patch endpoint 25269, Akka port 25299, and embedded database port 25280. `LocalWadCachePath`, embedded Raven data, copied SpiralDB data, mod data, and logs all pointed inside the isolated build output. The external `client-process-verify` mod was copied from its real Release build output into `mods/client-process-verify/`.

Command, run from the isolated pinned Director output directory:

```powershell
dotnet Imlight.Director.dll
```

With `[SpiralLoader] Enabled = true`, the real Director reported one mod found, one initialized, zero failed; created its login and game `ModMessageServiceHost` instances for the registered mod; bound the isolated ports; loaded Root.wad and the copied SpiralDB cache; and reached `Imlight may now be connected to.`

The first launch attempt used an empty isolated WAD cache and stopped at `Root.wad` because no local `Root` definition existed. I recovered by copying the existing 324,591,616-byte Imlight cache file into the isolated run as `data/localcache.db`. The original cache was not modified. The isolated patch endpoint remained unavailable and Imlight logged that it would continue without the patch server; cached resources loaded successfully.

The same Director was launched again with `[SpiralLoader] Enabled = false`. It logged `Imlight runs without mods`, did not start SpiralLoader, loaded the same resources, bound the isolated login/game ports, and reached the ready message. The process was stopped after readiness. No installed Wizard101 client was launched.

## Source changes and remaining boundary

- `integration/imlight/verify/ClientProcessMod/ClientProcessMod.csproj`: made the harness reference conditional so real Imlight references can be compiled without collision.
- `integration/imlight/README.md`: replaced stale “not yet built” status with the results of the two real builds and documented generated `ByteString` fields.
- `docs/building.md`: updated the documented test count from 150 to 153.
- `MILESTONE3_LOCAL_TEST_RESULTS.md`: this report.

No reproducible ModMessageService build, registration, routing, isolation, exception-containment, or session-disposal defect remains. A real client Lua round-trip at character select/in-game was not run; that needs a safe separate client setup, and the installed client was deliberately left untouched.

READY TO MERGE: real builds and external message-service integration verified.
