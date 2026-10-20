# CLAUDE.md — SpiralLoader

Modloader for Revive101's Imlight (Wizard101 private server, .NET 10, Akka.NET). Read `docs/architecture.md`
for design, `docs/audit.md` for Imlight and modding compatibility research, `docs/next-milestone.md` for the independent project roadmap.

## Layout and dependency rules

```
src/SpiralLoader.Api       public mod API — NO dependencies, ever. Mods reference it with Private=false.
src/SpiralLoader.Runtime   plugin manager — depends only on Api. Host-agnostic: no Imlight concepts.
src/SpiralLoader.Imlight   Imlight bootstrap — depends on Runtime, NOT on any Imlight assembly.
src/SpiralLoader.Host      headless host for dev/CI.
examples/                  example mods (built into artifacts/mods by the build); standalone-mod is an out-of-tree template, not in the sln.
tools/                     SpiralLoader.ApiSurface: prints/compares public API surfaces (shared source also linked into tests and the verify harness).
api-baseline/<set>/        recorded public surfaces + diagnostic codes of a release set (1.2.0). Exact-match target = newest dir; older dirs = must-not-break.
tests/fixtures/            sources of PREBUILT binaries committed under tests/SpiralLoader.Tests/Prebuilt (never rebuilt by the suite).
tests/SpiralLoader.Tests   xUnit. References examples/host only for build order (ReferenceOutputAssembly=false).
integration/imlight/       patches/ (git am series), setup + verify scripts, verify/ harness.
external/                  git-ignored third-party checkouts (Imlight). Has an MSBuild barrier; keep it.
```

Imlight-side code (anything that touches Imlight types) lives **only** in the patch series. Keep it minimal;
put logic in `SpiralLoader.Imlight` where it can be tested.

## Commands

```bash
dotnet build SpiralLoader.sln          # also assembles artifacts/mods/*
dotnet test SpiralLoader.sln           # 172 tests, ~10 s (one runs `dotnet build` on examples/standalone-mod: filter Category!=ExternalBuild to skip)
dotnet run --project src/SpiralLoader.Host -- --mods artifacts/mods --run "hello world" --plan-assets
dotnet run --project src/SpiralLoader.Host -- --check --mod <mod folder> [--imlight-api 1.2.0]   # developer compatibility check, exit 0/1
integration/imlight/verify/verify-patch.sh            # IMLIGHT_COMMIT=<sha> to test another Imlight commit
pwsh integration/imlight/verify/Verify-Patch.ps1      # Windows twin
```

In the cloud container the SDK may not be on PATH: `export PATH=/root/.dotnet:$PATH DOTNET_ROOT=/root/.dotnet`
(install with `dotnet-install.sh --channel 10.0 --install-dir /root/.dotnet`; pwsh via
`dotnet tool install --global PowerShell`).

## Hard constraints

- **Imlight.CoreLib cannot be compiled here.** Imcodec's source generators need client-derived inputs
  (WizWalker JSON type dump, message XMLs) that are not distributed. `Imlight.Common` does build. Never claim a
  CoreLib/Director build succeeded; the verify harness compiles only the files listed in
  `integration/imlight/README.md`. Say which call sites are compile-unverified.
- Never commit or download Wizard101 assets, client files, or Imcodec generator inputs.
- .NET 10 only; Windows is the primary platform (keep scripts working in Windows PowerShell 5.1 and pwsh; keep
  bash twins for CI).
- Unmodded Imlight behaviour must not change: every Imlight call site is guarded by `HasSubscribers<T>()` or
  returns built-ins unchanged when no loader is attached (`NullEventBus`, `EmptyRegistryProvider`).
- Respect Akka: event handlers run synchronously on the publishing actor's thread. No blocking APIs in hooks;
  no new cross-thread shared mutable state; registrations are frozen after load (lock-free reads).
- Mods are trusted code; never describe SpiralLoader as a sandbox.
- No assembly unloading. Contexts are non-collectible; changing mods requires restart.
- Do not invent Imlight APIs. Check the pinned source (`setup-imlight.sh` gives you a checkout) before using a
  type or member, and check its visibility (`MessageService`, `IComponentFactory`, `CommandProtocol`,
  `NumericRequirement` are internal). Mods get message services through the public `ModMessageService` (1.2.0).
- The verify harness compiles Imlight's real `SessionActor`/`MessageService`; never reintroduce a stub of them
  (a public stub `MessageService` once hid the fact that mods could not derive from the real one).

## Conventions

- C# in `src/`: file-scoped namespaces, K&R braces, `_camelCase` fields, `s_` statics, nullable enabled,
  warnings are errors (`Directory.Build.props`). Public API members get XML docs.
- Code inside the Imlight patch series follows Imlight's style instead: AGPL file header block, `{0}`-style
  `Logger` templates with `Logger.Args`, nullable disabled in CoreLib, no implicit usings, explicit `using`s
  sorted like the surrounding file, LF endings.
- Diagnostic codes in `src/SpiralLoader.Runtime/ModDiagnostic.cs` are stable and documented in
  `docs/architecture.md`; tests match codes, not messages. Add a code + doc row + a `DiagnosticHints` entry for every new
  failure mode (`ApiCompatibilityTests` enforces all three, and that no released code disappears).
- Determinism: anything ordered (discovery, resolution, dispatch, registries, asset plans) must not depend on
  filesystem or hash order.
- Tests that need real assemblies compile them with `TestCompiler` (Roslyn) into a `ModWorkspace`; compiled mods
  talk back through `TestJournal`.

## Changing the Imlight patch series

1. `integration/imlight/setup-imlight.sh` (or a checkout at the pinned commit on branch `spiralloader`).
2. Commit on top of the series (one concern per commit, conventional-commit subject).
3. `integration/imlight/regen-patches.sh` (deletes the old files and runs
   `git format-patch b07987168fcfa2d123a5d78df90ca0c76405e427..HEAD -o integration/imlight/patches/`, so numbering
   stays contiguous).
4. Run both verify scripts for the pinned commit **and** `IMLIGHT_COMMIT=<upstream quality-assurance HEAD>`.
5. Extend `verify/Stubs.cs` / `ImcodecStubs.cs` and the harness `.csproj` to compile any newly touched file that
   can compile against stubs, drive it in `verify/Scenarios.cs`, add the expected lines to
   `verify/expected-output.txt` (shared by both verify scripts; `expected-output-<commit12>.txt` for
   revision-specific lines, `expected-vanilla-output.txt` for the run with the client-process mods disabled, `!` =
   must not appear), and update the verification table in
   `integration/imlight/README.md`. Do not stub the file under test.
6. Bump `ImlightModding.IntegrationApiVersion` (minor for additions, major for breaks) and document hooks in
   `docs/mod-authoring.md`. The harness prints the public `Imlight.CoreLib.Modding` surface and both verify scripts compare it
   with `api-baseline/<newest>/Imlight.CoreLib.Modding.txt`: record a new baseline set as `docs/api-compatibility.md` describes.

## Changing the public API

`SpiralLoader.Api` is compared with `api-baseline/` by `ApiCompatibilityTests`. Removing/changing a member, or changing a registry
key name, event semantics or manifest meaning, is breaking; follow `docs/api-compatibility.md` (SemVer table, changelog, new
baseline, new `AssemblyVersion`). Do not claim the API is frozen or stable.

## Integration expectations

- Mods load in `Program.Main` right after `ConfigurationManager.Initialize`, before the actor system and
  before Imlight's AppDomain-scanning registries run.
- Default failure policy is `Abort`: a broken mod stops server startup with the report in the log.
- Built-in dependency ids: `spiralloader` (API version), `dotnet`, `imlight` (integration API version).
- Spiral Shuffle is independent compatibility research only; it is not a porting target or acceptance requirement. The pinned Imlight revision is `b07987168fcfa2d123a5d78df90ca0c76405e427`.
