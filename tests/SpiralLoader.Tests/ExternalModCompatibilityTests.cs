using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using SpiralLoader.Api.Commands;
using SpiralLoader.Api.Events;
using SpiralLoader.Api.Versioning;
using SpiralLoader.Runtime;
using SpiralLoader.Tests.Infrastructure;

namespace SpiralLoader.Tests;

/// <summary>
/// What a third-party mod author can rely on, shown with mods that were compiled independently of the runtime:
/// a project built outside this repository with the plain <c>dotnet</c> CLI, a binary committed earlier and never
/// rebuilt, and assemblies whose metadata claims other API versions. The scenario catalogue in
/// docs/compatibility-testing.md maps every required scenario to the test that covers it, reusing the existing
/// suites; this class holds only what they did not cover.
/// </summary>
public sealed partial class ExternalModCompatibilityTests : IDisposable {

    private const string FixtureApiVersion = "0.1.0.0";

    private readonly ModWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private static string TemplateDirectory => Path.Combine(BuildPaths.RepoRoot, "examples", "standalone-mod");

    private static Version? ReferencedApiVersion(string assemblyPath) {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.AssemblyReferences) {
            var reference = metadata.GetAssemblyReference(handle);
            if (metadata.GetString(reference.Name) == "SpiralLoader.Api") {
                return reference.Version;
            }
        }
        return null;
    }

    private static void CopyDirectory(string source, string destination) {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    // ---- A correctly packaged external mod ---------------------------------------------------------------

    [Fact]
    [Trait("Category", "ExternalBuild")]
    public async Task ModBuiltOutsideTheRepositoryWithThePlainDotnetCliLoadsCleanly() {
        // Copy the template somewhere with no SpiralLoader Directory.Build.props above it, as a third party would have.
        using var source = new TempDirectory();
        foreach (var file in Directory.EnumerateFiles(TemplateDirectory).Where(f => !f.EndsWith(".user", StringComparison.Ordinal))) {
            File.Copy(file, source.Combine(Path.GetFileName(file)));
        }

        var modFolder = _workspace.ModDirectory("standalone-mod");
        var build = await ProcessRunner.RunAsync(ProcessRunner.DotNet, source.Path, "build", "StandaloneMod.csproj", "-c", "Release", "-o", modFolder,
            $"-p:SpiralLoaderApiPath={typeof(SpiralLoader.Api.ISpiralMod).Assembly.Location}", "-nologo");
        Assert.True(build.ExitCode == 0, build.Output);

        // Packaging: the mod folder carries the manifest and the mod, never the host's API.
        Assert.True(File.Exists(Path.Combine(modFolder, "spiralmod.json")));
        Assert.True(File.Exists(Path.Combine(modFolder, "StandaloneMod.dll")));
        Assert.True(File.Exists(Path.Combine(modFolder, "StandaloneMod.deps.json")));
        Assert.False(File.Exists(Path.Combine(modFolder, "SpiralLoader.Api.dll")));
        Assert.Equal(new Version(FixtureApiVersion), ReferencedApiVersion(Path.Combine(modFolder, "StandaloneMod.dll")));

        var host = _workspace.CreateHost();
        host.AddRegistry(CommonRegistries.Commands, ModCommandRouter.Validate);
        var report = host.Load();

        Assert.False(report.HasFailures, report.Format());
        Assert.Equal(["standalone-mod"], report.LoadOrder);
        Assert.Empty(report.Find("standalone-mod")!.Diagnostics);
        var router = new ModCommandRouter(host.Registries.Get(CommonRegistries.Commands).Entries, host.Logger);
        Assert.True(router.TryExecute("standalone Ada", "tester", callerIsOperator: false, out var reply));
        Assert.Equal("standalone-mod says hello to Ada.", reply);

        // The same folder passes the developer-facing check command, out of process.
        var check = await ProcessRunner.RunHostAsync(source.Path, "--check", "--mod", modFolder);
        Assert.True(check.ExitCode == 0, check.Output);
        Assert.Contains("compatibility check: PASS", check.Output);
    }

    // ---- A previously compiled mod, no rebuild ------------------------------------------------------------

    [Fact]
    public async Task ModCompiledAgainstTheBaselineApiLoadsAndPassesItsTourWithoutRebuilding() {
        var prebuilt = Path.Combine(BuildPaths.PrebuiltDirectory, "api-0.1.0", "api-tour");
        // Proof that it really is an older binary: its metadata records the baseline API assembly version and it
        // ships no copy of the API.
        Assert.Equal(new Version(FixtureApiVersion), ReferencedApiVersion(Path.Combine(prebuilt, "ApiTour.dll")));
        Assert.False(File.Exists(Path.Combine(prebuilt, "SpiralLoader.Api.dll")));
        CopyDirectory(prebuilt, _workspace.ModDirectory("api-tour"));

        var host = _workspace.CreateHost();
        host.AddRegistry(CommonRegistries.Commands, ModCommandRouter.Validate);
        var report = host.Load();
        host.Events.Publish(new ServerStartingEvent("TestHost"));
        host.Events.Publish(new ServerResourcesLoadedEvent("TestHost"));
        host.Events.Publish(new ServerStartedEvent("TestHost"));

        Assert.False(report.HasFailures, report.Format());
        var mod = report.Find("api-tour")!;
        Assert.Equal(ModState.Initialized, mod.State);
        // Only the declared optional recommendation is reported; the runtime raised no api_older/api_newer note.
        Assert.Equal([DiagnosticCodes.RecommendationMissing], mod.Diagnostics.Select(d => d.Code));

        var router = new ModCommandRouter(host.Registries.Get(CommonRegistries.Commands).Entries, host.Logger);
        Assert.True(router.TryExecute("tour", "tester", callerIsOperator: false, out var reply));
        Assert.Equal("tour ok; lifecycle: loaded:api-tour starting:TestHost resources:TestHost started:TestHost", reply);

        host.Events.Publish(new ServerStoppingEvent("TestHost"));
        host.Shutdown();

        // And through the headless host, the way a mod developer would run it.
        using var work = new TempDirectory();
        var check = await ProcessRunner.RunHostAsync(work.Path, "--check", "--mod", prebuilt, "--run", "tour");
        Assert.True(check.ExitCode == 0, check.Output);
        Assert.Contains("tour ok;", check.Output);
    }

    // ---- API assembly versions recorded in a mod's metadata -------------------------------------------------

    private string StandInApi(string version) {
        // Next to the mods directory, not inside it: the stand-in is only a compile-time reference.
        var path = Path.Combine(Path.GetDirectoryName(_workspace.ModsDirectory)!, $"standin-{version}", "SpiralLoader.Api.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        TestCompiler.Compile("SpiralLoader.Api", $$"""
            [assembly: System.Reflection.AssemblyVersion("{{version}}")]
            namespace SpiralLoader.Api;
            public interface IModContext { }
            public interface ISpiralMod { void Initialize(IModContext context); }
            """, path, referenceHostApi: false);
        return path;
    }

    private void AddModBuiltAgainst(string id, string apiVersion, string channel) {
        var directory = _workspace.ModDirectory(id);
        Directory.CreateDirectory(directory);
        TestCompiler.Compile("Built" + id.Replace("-", ""), $$"""
            using SpiralLoader.Api;
            namespace Built;
            public sealed class Mod : ISpiralMod {
                public void Initialize(IModContext context) => SpiralLoader.Tests.Infrastructure.TestJournal.Record("{{channel}}", "ran");
            }
            """, Path.Combine(directory, "Built.dll"), [StandInApi(apiVersion), TestJournal.AssemblyPath], referenceHostApi: false);
        _workspace.AddManifest(id, new Dictionary<string, object> {
            ["schemaVersion"] = 1, ["id"] = id, ["version"] = "1.0.0",
            ["assemblies"] = new[] { "Built.dll" }, ["entrypoints"] = new Dictionary<string, string[]> { ["server"] = ["Built.Mod"] },
        });
    }

    [Fact]
    public void ModBuiltAgainstANewerApiIsRefusedBeforeItsCodeRuns() {
        var channel = TestJournal.NewChannel();
        AddModBuiltAgainst("from-the-future", "9.0.0.0", channel);

        var report = _workspace.Load();

        var mod = report.Find("from-the-future")!;
        Assert.Equal(ModState.Failed, mod.State);
        var diagnostic = Assert.Single(mod.Diagnostics, d => d.Code == DiagnosticCodes.AssemblyApiNewer);
        Assert.Contains("9.0.0.0", diagnostic.Message);
        Assert.Contains(FixtureApiVersion, diagnostic.Message);
        Assert.Empty(TestJournal.Read(channel));
        Assert.Empty(mod.Assemblies);
    }

    [Fact]
    public void ModBuiltAgainstAnOlderApiStillRunsAndIsToldItMayNeedARebuild() {
        var channel = TestJournal.NewChannel();
        AddModBuiltAgainst("from-the-past", "0.0.1.0", channel);

        var report = _workspace.Load();

        var mod = report.Find("from-the-past")!;
        Assert.Equal(ModState.Initialized, mod.State);
        var warning = Assert.Single(mod.Diagnostics);
        Assert.Equal(DiagnosticCodes.AssemblyApiOlder, warning.Code);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(["ran"], TestJournal.Read(channel));
    }

    // ---- Disabling an incompatible mod ----------------------------------------------------------------------

    [Fact]
    public void DisablingAnIncompatibleModLetsTheServerStartWithoutItOrItsDependents() {
        var channel = TestJournal.NewChannel();
        string Recording(string ns) => $$"""
            using SpiralLoader.Api;
            namespace {{ns}};
            public sealed class Mod : ISpiralMod {
                public void Initialize(IModContext context) => SpiralLoader.Tests.Infrastructure.TestJournal.Record("{{channel}}", "{{ns}}");
            }
            """;
        string[] journal = [TestJournal.AssemblyPath];
        _workspace.AddCodeMod("steady", "Steady", Recording("Steady"), ["Steady.Mod"], references: journal);
        // Needs an Imlight integration API the host does not have.
        _workspace.AddCodeMod("needs-newer-imlight", "Newer", Recording("Newer"), ["Newer.Mod"], new() {
            ["depends"] = new Dictionary<string, string> { ["imlight"] = "^1.3.0" },
        }, journal);
        _workspace.AddCodeMod("builds-on-it", "BuildsOn", Recording("BuildsOn"), ["BuildsOn.Mod"], new() {
            ["depends"] = new Dictionary<string, string> { ["needs-newer-imlight"] = "*" },
        }, journal);
        void Imlight12(ModHostOptions o) {
            o.BuiltIns.Add(new BuiltInMod("imlight", SemanticVersion.Parse("1.2.0"), "Imlight integration API"));
            o.FailurePolicy = ModFailurePolicy.AbortStartup;
        }

        // Default policy: the server refuses to start, and the report names the mod, the code and the cause.
        var abort = Assert.Throws<ModLoadException>(() => _workspace.CreateHost(Imlight12).Load());
        Assert.Contains("needs-newer-imlight", abort.Message);
        Assert.Contains(DiagnosticCodes.DependencyVersionMismatch, abort.Message);
        Assert.Contains("^1.3.0", abort.Message);
        Assert.Equal(["Steady"], TestJournal.Read(channel));   // the compatible mod initialized (and was shut down again); the rejected ones never ran

        // Disabling only the incompatible mod is not enough: whatever depends on it is rejected, and says so.
        var partial = Assert.Throws<ModLoadException>(() => _workspace.CreateHost(o => { Imlight12(o); o.DisabledMods.Add("needs-newer-imlight"); }).Load());
        Assert.Equal(ModState.Disabled, partial.Report.Find("needs-newer-imlight")!.State);
        var dependent = partial.Report.Find("builds-on-it")!;
        Assert.Contains(dependent.Diagnostics, d => d.Code == DiagnosticCodes.DependencyRejected && d.Message.Contains("disabled"));

        // Disabling both starts the server with the mods that are compatible, and nothing of the others ever runs.
        var host = _workspace.CreateHost(o => { Imlight12(o); o.DisabledMods.Add("needs-newer-imlight"); o.DisabledMods.Add("builds-on-it"); });
        var report = host.Load();
        Assert.False(report.HasFailures, report.Format());
        Assert.Equal(["steady"], report.LoadOrder);
        Assert.Equal(["Steady", "Steady", "Steady"], TestJournal.Read(channel));   // once per load above; never "Newer" or "BuildsOn"
        Assert.Empty(report.Find("needs-newer-imlight")!.Assemblies);
        Assert.Equal(ModState.Disabled, report.Find("builds-on-it")!.State);
    }

    // ---- The compatibility check command -------------------------------------------------------------------

    [Fact]
    public async Task CheckCommandReportsFailuresWithCodesAndFixesAndDisablingMakesItPass() {
        _workspace.AddDataMod("fine");
        _workspace.AddDataMod("needs-imlight", extra: new() { ["depends"] = new Dictionary<string, string> { ["imlight"] = "^1.2.0" } });
        using var work = new TempDirectory();

        var failing = await ProcessRunner.RunHostAsync(work.Path, "--check", "--mods", _workspace.ModsDirectory);
        Assert.Equal(1, failing.ExitCode);
        Assert.Contains("compatibility check: FAIL", failing.Output);
        Assert.Contains("needs-imlight 1.0.0: Rejected (dependency.missing)", failing.Output);
        Assert.Contains("fine 1.0.0:", failing.Output);
        Assert.Contains("fix:", failing.Output);
        Assert.Contains("--imlight-api", failing.Output);

        var simulated = await ProcessRunner.RunHostAsync(work.Path, "--check", "--mods", _workspace.ModsDirectory, "--imlight-api", "1.2.0");
        Assert.True(simulated.ExitCode == 0, simulated.Output);
        Assert.Contains("compatibility check: PASS", simulated.Output);
        Assert.Contains("imlight integration API 1.2.0 (simulated", simulated.Output);

        var tooOld = await ProcessRunner.RunHostAsync(work.Path, "--check", "--mods", _workspace.ModsDirectory, "--imlight-api", "1.1.0");
        Assert.Equal(1, tooOld.ExitCode);
        Assert.Contains("dependency.version_mismatch", tooOld.Output);

        var disabled = await ProcessRunner.RunHostAsync(work.Path, "--check", "--mods", _workspace.ModsDirectory, "--disable", "needs-imlight");
        Assert.True(disabled.ExitCode == 0, disabled.Output);
        Assert.Contains("needs-imlight 1.0.0: Disabled", disabled.Output);
    }

    [Fact]
    public async Task CheckCommandExplainsAFolderThatIsNotAModAndAnEmptyModsDirectory() {
        using var work = new TempDirectory();
        Directory.CreateDirectory(work.Combine("not-a-mod"));

        var notAMod = await ProcessRunner.RunHostAsync(work.Path, "--check", "--mod", work.Combine("not-a-mod"));
        Assert.Equal(1, notAMod.ExitCode);
        Assert.Contains("does not contain spiralmod.json", notAMod.Output);

        var empty = await ProcessRunner.RunHostAsync(work.Path, "--check", "--mods", work.Combine("not-a-mod"));
        Assert.Equal(0, empty.ExitCode);
        Assert.Contains("no mods were found", empty.Output);
    }

    [Fact]
    public async Task CheckCommandCanTreatWarningsAsFailures() {
        _workspace.AddDataMod("chatty", extra: new() { ["recommends"] = new Dictionary<string, string> { ["absent"] = "*" } });
        using var work = new TempDirectory();

        Assert.Equal(0, (await ProcessRunner.RunHostAsync(work.Path, "--check", "--mods", _workspace.ModsDirectory)).ExitCode);
        var strict = await ProcessRunner.RunHostAsync(work.Path, "--check", "--mods", _workspace.ModsDirectory, "--warnings-as-errors");
        Assert.Equal(1, strict.ExitCode);
        Assert.Contains("chatty: warning recommends.missing", strict.Output);
    }

    // ---- Diagnostics: which mod, what, which code, what to do ----------------------------------------------

    private sealed record Scenario(string Name, string ModId, string Code, string[] Fragments, Action<ModWorkspace> Arrange, Action<ModHostOptions>? Configure = null);

    private static IEnumerable<Scenario> Scenarios() {
        yield return new("missing dependency", "needy", DiagnosticCodes.DependencyMissing,
            ["'ghost'", "^1.0.0", "built-ins", "spiralloader 0.1.0"],
            w => w.AddDataMod("needy", extra: new() { ["depends"] = new Dictionary<string, string> { ["ghost"] = "^1.0.0" } }));

        yield return new("host too old for the mod", "wants-newer-loader", DiagnosticCodes.DependencyVersionMismatch,
            ["'spiralloader' >=9.0.0", "0.1.0 is present (provided by the host)"],
            w => w.AddDataMod("wants-newer-loader", extra: new() { ["depends"] = new Dictionary<string, string> { ["spiralloader"] = ">=9.0.0" } }));

        yield return new("mod too old for its dependent", "consumer", DiagnosticCodes.DependencyVersionMismatch,
            ["'library' ^2.0.0", "1.4.0 is present (installed at"],
            w => {
                w.AddDataMod("library", "1.4.0");
                w.AddDataMod("consumer", extra: new() { ["depends"] = new Dictionary<string, string> { ["library"] = "^2.0.0" } });
            });

        yield return new("duplicate mod id", "twin", DiagnosticCodes.ModDuplicateId,
            ["copy-a", "copy-b"],
            w => {
                w.AddManifest("copy-a", new { schemaVersion = 1, id = "twin", version = "1.0.0" });
                w.AddManifest("copy-b", new { schemaVersion = 1, id = "twin", version = "1.1.0" });
            });

        yield return new("unsupported manifest schema", "from-tomorrow", DiagnosticCodes.ManifestUnsupportedSchema,
            ["schemaVersion 2", "reads version 1"],
            w => w.AddManifest("from-tomorrow", new { schemaVersion = 2, id = "from-tomorrow", version = "1.0.0" }));

        yield return new("declared assembly is missing", "no-dll", DiagnosticCodes.AssemblyMissing,
            ["'Gone.dll'"],
            w => w.AddManifest("no-dll", new { schemaVersion = 1, id = "no-dll", version = "1.0.0", assemblies = new[] { "Gone.dll" } }));

        yield return new("entrypoint typo names the real entrypoints", "typo", DiagnosticCodes.EntrypointNotFound,
            ["'Typo.Moda'", "Typo.Mod", "ISpiralMod"],
            w => w.AddCodeMod("typo", "Typo", """
                using SpiralLoader.Api;
                namespace Typo;
                public sealed class Mod : ISpiralMod { public void Initialize(IModContext context) { } }
                """, ["Typo.Moda"]));

        yield return new("initialization failure states the cause and the rollback", "explodes", DiagnosticCodes.InitializationFailed,
            ["Explodes.Mod.Initialize threw InvalidOperationException: outer", "caused by TimeoutException: inner cause", "Rolled back 1 subscription(s) and 0 registration(s)"],
            w => w.AddCodeMod("explodes", "Explodes", """
                using SpiralLoader.Api;
                using SpiralLoader.Api.Events;
                namespace Explodes;
                public sealed class Mod : ISpiralMod {
                    public void Initialize(IModContext context) {
                        context.Events.Subscribe<ServerStartedEvent>(_ => { });
                        throw new System.InvalidOperationException("outer", new System.TimeoutException("inner cause"));
                    }
                }
                """, ["Explodes.Mod"]));

        yield return new("pinned to another Imlight commit", "pinned", DiagnosticCodes.CompatibilityImlightCommit,
            ["WarnOnly", new string('a', 40), new string('b', 40)],
            w => w.AddDataMod("pinned", extra: new() { ["compatibility"] = new Dictionary<string, string[]> { ["imlightCommits"] = [new string('a', 40)] } }),
            o => o.ImlightCommit = new string('b', 40));
    }

    [Fact]
    public void EveryFailureNamesTheModTheCodeTheCauseAndWhatToDo() {
        foreach (var scenario in Scenarios()) {
            using var workspace = new ModWorkspace();
            scenario.Arrange(workspace);

            var report = workspace.Load(scenario.Configure);
            var text = report.Format();
            var mod = report.Find(scenario.ModId);
            var context = $"[{scenario.Name}]{Environment.NewLine}{text}";

            Assert.True(mod is not null, context);
            var diagnostic = mod!.Diagnostics.FirstOrDefault(d => d.Code == scenario.Code);
            Assert.True(diagnostic is not null, context);
            Assert.True(diagnostic!.Hint is { Length: > 20 }, context);
            Assert.Contains($"Needs attention: ", text);                       // the summary names the mod and the code up front...
            Assert.Contains(scenario.ModId, text.Split('\n').First(l => l.Contains("Needs attention")));
            Assert.Contains(scenario.Code, text.Split('\n').First(l => l.Contains("Needs attention")));
            Assert.True(text.Contains($"{diagnostic.Code}: ", StringComparison.Ordinal) && text.Contains("fix: " + diagnostic.Hint, StringComparison.Ordinal), context);
            foreach (var fragment in scenario.Fragments) {
                Assert.True(text.Contains(fragment, StringComparison.Ordinal), $"Missing '{fragment}' in {context}");
            }
        }
    }

    [Fact]
    public void TheStartupAbortCarriesTheSameActionableReport() {
        _workspace.AddDataMod("needy", extra: new() { ["depends"] = new Dictionary<string, string> { ["ghost"] = "*" } });

        var exception = Assert.Throws<ModLoadException>(() => _workspace.CreateHost(o => o.FailurePolicy = ModFailurePolicy.AbortStartup).Load());

        Assert.Contains("needy 1.0.0 [Rejected: dependency.missing]", exception.Message);
        Assert.Contains("fix:", exception.Message);
        Assert.Contains(Path.Combine(_workspace.ModsDirectory, "needy"), exception.Message);
    }

    // ---- The scenario catalogue stays true ---------------------------------------------------------------------

    [GeneratedRegex(@"`(?<type>[A-Za-z]+Tests)\.(?<method>[A-Za-z0-9_]+)`")]
    private static partial Regex TestReference();

    [Fact]
    public void EveryTestNamedInTheCompatibilityGuideExists() {
        var guide = File.ReadAllText(Path.Combine(BuildPaths.RepoRoot, "docs", "compatibility-testing.md"));
        var references = TestReference().Matches(guide).Select(m => (Type: m.Groups["type"].Value, Method: m.Groups["method"].Value)).Distinct().ToList();

        Assert.True(references.Count >= 15, "The scenario catalogue should name the tests that cover each scenario.");
        var assembly = typeof(ExternalModCompatibilityTests).Assembly;
        foreach (var (typeName, method) in references) {
            var type = assembly.GetType("SpiralLoader.Tests." + typeName);
            Assert.True(type is not null, $"docs/compatibility-testing.md names {typeName}.{method}, but there is no test class {typeName}.");
            Assert.True(type!.GetMethod(method, BindingFlags.Public | BindingFlags.Instance) is not null, $"docs/compatibility-testing.md names {typeName}.{method}, which does not exist.");
        }
    }

}
