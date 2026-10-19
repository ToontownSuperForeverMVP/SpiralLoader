using SpiralLoader.Api.Commands;
using SpiralLoader.Api.Events;
using SpiralLoader.Runtime;
using SpiralLoader.Tests.Infrastructure;

namespace SpiralLoader.Tests;

/// <summary>
/// Loads assemblies compiled at test time, so assembly loading, entrypoint construction and initialization
/// failures are exercised with real code rather than mocks.
/// </summary>
public sealed class LoadingTests : IDisposable {

    private readonly ModWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private static string[] JournalReference => [TestJournal.AssemblyPath];

    private static string RecordingMod(string ns, string channel, string body = "") => $$"""
        using SpiralLoader.Api;
        using SpiralLoader.Tests.Infrastructure;
        namespace {{ns}};
        public sealed class Mod : ISpiralMod {
            public void Initialize(IModContext context) {
                TestJournal.Record("{{channel}}", "init:" + context.Manifest.Id + ":" + string.Join(",", context.Mods.LoadOrder));
                {{body}}
            }
            public void Shutdown() => TestJournal.Record("{{channel}}", "shutdown:{{ns}}");
        }
        """;

    [Fact]
    public void InitializesEntrypointsInDependencyOrder() {
        var channel = TestJournal.NewChannel();
        _workspace.AddCodeMod("second", "OrderSecond", RecordingMod("OrderSecond", channel), ["OrderSecond.Mod"],
            new() { ["depends"] = new Dictionary<string, string> { ["first"] = "1.x" } }, JournalReference);
        _workspace.AddCodeMod("first", "OrderFirst", RecordingMod("OrderFirst", channel), ["OrderFirst.Mod"],
            references: JournalReference);

        var host = _workspace.CreateHost();
        var report = host.Load();

        Assert.Equal(["first", "second"], report.LoadOrder);
        // Each mod sees the mods initialized before it.
        Assert.Equal(["init:first:", "init:second:first"], TestJournal.Read(channel));

        host.Shutdown();
        Assert.Equal(["shutdown:OrderSecond", "shutdown:OrderFirst"], TestJournal.Read(channel).Skip(2));
        Assert.All(report.Mods, m => Assert.Equal(ModState.ShutDown, m.State));
    }

    [Fact]
    public void InitializationFailureRollsBackRegistrationsAndFailsDependents() {
        var channel = TestJournal.NewChannel();
        _workspace.AddCodeMod("faulty", "Faulty", $$"""
            using SpiralLoader.Api;
            using SpiralLoader.Api.Commands;
            using SpiralLoader.Api.Events;
            namespace Faulty;
            public sealed class Mod : ISpiralMod {
                public void Initialize(IModContext context) {
                    context.Events.Subscribe<ServerStartedEvent>(_ => SpiralLoader.Tests.Infrastructure.TestJournal.Record("{{channel}}", "faulty handler ran"));
                    context.Registries.Get(CommonRegistries.Commands).Register("boom", new ModCommand("boom", "", _ => "x"));
                    throw new System.InvalidOperationException("init exploded");
                }
            }
            """, ["Faulty.Mod"], references: JournalReference);
        _workspace.AddCodeMod("dependent", "Dependent", RecordingMod("Dependent", channel), ["Dependent.Mod"],
            new() { ["depends"] = new Dictionary<string, string> { ["faulty"] = "*" } }, JournalReference);
        _workspace.AddCodeMod("unrelated", "Unrelated", RecordingMod("Unrelated", channel), ["Unrelated.Mod"],
            references: JournalReference);

        var host = _workspace.CreateHost();
        host.AddRegistry(CommonRegistries.Commands, ModCommandRouter.Validate);
        var report = host.Load();

        var faulty = report.Find("faulty")!;
        Assert.Equal(ModState.Failed, faulty.State);
        Assert.Contains(faulty.Diagnostics, d => d.Code == DiagnosticCodes.InitializationFailed && d.Message.Contains("init exploded"));

        var dependent = report.Find("dependent")!;
        Assert.Equal(ModState.Failed, dependent.State);
        Assert.Contains(dependent.Diagnostics, d => d.Code == DiagnosticCodes.DependencyFailed);

        Assert.Equal(["unrelated"], report.LoadOrder);
        Assert.Equal(["init:unrelated:"], TestJournal.Read(channel));

        // The failed mod's subscription and registration were removed.
        Assert.Empty(host.Registries.Get(CommonRegistries.Commands).Entries);
        Assert.False(host.Events.HasSubscribers<ServerStartedEvent>());
        host.Events.Publish(new ServerStartedEvent("TestHost"));
        Assert.DoesNotContain("faulty handler ran", TestJournal.Read(channel));
        Assert.Contains(_workspace.Log.Entries, e => e.Message.Contains("Rolled back 1 subscription(s) and 1 registration(s)"));
    }

    [Fact]
    public void ConstructorFailureIsReported() {
        _workspace.AddCodeMod("ctor-throws", "CtorThrows", """
            using SpiralLoader.Api;
            namespace CtorThrows;
            public sealed class Mod : ISpiralMod {
                public Mod() => throw new System.NotSupportedException("no ctor for you");
                public void Initialize(IModContext context) { }
            }
            """, ["CtorThrows.Mod"]);

        var mod = _workspace.Load().Find("ctor-throws")!;

        Assert.Equal(ModState.Failed, mod.State);
        Assert.Contains(mod.Diagnostics, d => d.Code == DiagnosticCodes.InitializationFailed && d.Message.Contains("no ctor for you"));
    }

    [Theory]
    [InlineData("Missing.Type", DiagnosticCodes.EntrypointNotFound, "was not found")]
    [InlineData("BadEntrypoints.NotAMod", DiagnosticCodes.EntrypointInvalid, "does not implement")]
    [InlineData("BadEntrypoints.AbstractMod", DiagnosticCodes.EntrypointInvalid, "non-abstract")]
    [InlineData("BadEntrypoints.NeedsArguments", DiagnosticCodes.EntrypointInvalid, "parameterless")]
    public void RejectsInvalidEntrypoints(string entrypoint, string code, string fragment) {
        _workspace.AddCodeMod("bad-entry", "BadEntrypoints", """
            using SpiralLoader.Api;
            namespace BadEntrypoints;
            public sealed class NotAMod { }
            public abstract class AbstractMod : ISpiralMod { public void Initialize(IModContext context) { } }
            public sealed class NeedsArguments(int value) : ISpiralMod { public void Initialize(IModContext context) { _ = value; } }
            """, [entrypoint]);

        var mod = _workspace.Load().Find("bad-entry")!;

        Assert.Equal(ModState.Failed, mod.State);
        Assert.Contains(mod.Diagnostics, d => d.Code == code && d.Message.Contains(fragment));
    }

    [Fact]
    public void CorruptAssemblyFailsOnlyThatMod() {
        var directory = _workspace.AddManifest("corrupt", new {
            schemaVersion = 1,
            id = "corrupt",
            version = "1.0.0",
            assemblies = new[] { "Corrupt.dll" },
            entrypoints = new { server = "Corrupt.Mod" },
        });
        File.WriteAllText(Path.Combine(directory, "Corrupt.dll"), "this is not a PE file");
        _workspace.AddDataMod("fine");

        var report = _workspace.Load();

        Assert.Equal(ModState.Failed, report.Find("corrupt")!.State);
        Assert.Contains(report.Find("corrupt")!.Diagnostics, d => d.Code == DiagnosticCodes.AssemblyLoadFailed);
        Assert.Equal(["fine"], report.LoadOrder);
    }

    [Fact]
    public void UnresolvableReferenceFailsAtLoadNotLater() {
        // Compile a library, reference it from the mod, then do not ship the library.
        using var scratch = new TempDirectory();
        var libraryPath = scratch.Combine("GhostLibrary.dll");
        TestCompiler.Compile("GhostLibrary", "namespace GhostLibrary; public class Base { }", libraryPath);
        _workspace.AddCodeMod("missing-ref", "MissingRef", """
            using SpiralLoader.Api;
            namespace MissingRef;
            public sealed class Derived : GhostLibrary.Base { }
            public sealed class Mod : ISpiralMod { public void Initialize(IModContext context) { } }
            """, ["MissingRef.Mod"], references: [libraryPath]);

        var mod = _workspace.Load().Find("missing-ref")!;

        Assert.Equal(ModState.Failed, mod.State);
        Assert.Contains(mod.Diagnostics, d => d.Code == DiagnosticCodes.AssemblyLoadFailed && d.Message.Contains("GhostLibrary"));
    }

    [Fact]
    public void PrivateDependencyShippedWithModIsLoaded() {
        using var scratch = new TempDirectory();
        var libraryPath = scratch.Combine("PrivateHelper.dll");
        TestCompiler.Compile("PrivateHelper", "namespace PrivateHelper; public static class Helper { public static string Name => \"helper\"; }", libraryPath);
        var channel = TestJournal.NewChannel();
        var directory = _workspace.AddCodeMod("with-helper", "WithHelper", $$"""
            using SpiralLoader.Api;
            namespace WithHelper;
            public sealed class Mod : ISpiralMod {
                public void Initialize(IModContext context)
                    => SpiralLoader.Tests.Infrastructure.TestJournal.Record("{{channel}}", PrivateHelper.Helper.Name);
            }
            """, ["WithHelper.Mod"], references: [libraryPath, TestJournal.AssemblyPath]);
        File.Copy(libraryPath, Path.Combine(directory, "PrivateHelper.dll"));

        var report = _workspace.Load();

        Assert.False(report.HasFailures, report.Format());
        Assert.Equal(["helper"], TestJournal.Read(channel));
    }

    [Fact]
    public void DependentModSharesTypesWithItsDependency() {
        var channel = TestJournal.NewChannel();
        var providerDirectory = _workspace.AddCodeMod("provider", "ProviderApi", """
            using SpiralLoader.Api;
            namespace ProviderApi;
            public sealed class Ping { public string Text { get; set; } = "ping"; }
            public static class Counter { public static int Value; }
            public sealed class Mod : ISpiralMod {
                public void Initialize(IModContext context) => Counter.Value = 41;
            }
            """, ["ProviderApi.Mod"]);
        var providerAssembly = Path.Combine(providerDirectory, "ProviderApi.dll");
        var consumerDirectory = _workspace.AddCodeMod("consumer", "ProviderConsumer", $$"""
            using SpiralLoader.Api;
            namespace ProviderConsumer;
            public sealed class Mod : ISpiralMod {
                public void Initialize(IModContext context) {
                    ProviderApi.Counter.Value++;
                    context.Events.Subscribe<ProviderApi.Ping>(p => p.Text += "-pong");
                    SpiralLoader.Tests.Infrastructure.TestJournal.Record("{{channel}}", ProviderApi.Counter.Value.ToString());
                }
            }
            """, ["ProviderConsumer.Mod"],
            new() { ["depends"] = new Dictionary<string, string> { ["provider"] = "*" } },
            [providerAssembly, TestJournal.AssemblyPath]);
        // Even if the consumer ships its own copy of the provider assembly, the provider's loaded copy wins.
        File.Copy(providerAssembly, Path.Combine(consumerDirectory, "ProviderApi.dll"));

        var host = _workspace.CreateHost();
        var report = host.Load();

        Assert.False(report.HasFailures, report.Format());
        // 41 set by the provider, incremented by the consumer: one static field, so one shared assembly.
        Assert.Equal(["42"], TestJournal.Read(channel));

        var provider = report.Find("provider")!.Assemblies.Single();
        var consumer = report.Find("consumer")!.Assemblies.Single();
        var referenced = consumer.GetReferencedAssemblies().Single(a => a.Name == "ProviderApi");
        Assert.Same(provider, System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(consumer)!.LoadFromAssemblyName(referenced));
    }

    [Fact]
    public void ModShippingItsOwnApiCopyStillBindsToHostApi() {
        var channel = TestJournal.NewChannel();
        var directory = _workspace.AddCodeMod("api-bundler", "ApiBundler",
            RecordingMod("ApiBundler", channel), ["ApiBundler.Mod"], references: JournalReference);
        File.Copy(typeof(SpiralLoader.Api.ISpiralMod).Assembly.Location, Path.Combine(directory, "SpiralLoader.Api.dll"));

        var mod = _workspace.Load().Find("api-bundler")!;

        Assert.Equal(ModState.Initialized, mod.State);
        Assert.Contains(mod.Diagnostics, d => d.Code == DiagnosticCodes.AssemblySharedCopy);
        Assert.Single(TestJournal.Read(channel));
    }

    [Fact]
    public void ModsSeeEnvironmentScopedToProfile() {
        var channel = TestJournal.NewChannel();
        _workspace.AddCodeMod("env-probe", "EnvProbe", RecordingMod("EnvProbe", channel, $$"""
            TestJournal.Record("{{channel}}", context.Environment.DataDirectory);
            TestJournal.Record("{{channel}}", context.Environment.Profile + "|" + context.Environment.HostName + "|" + context.Environment.EntrypointKind);
            """), ["EnvProbe.Mod"], references: JournalReference);

        _workspace.Load(o => o.Profile = "speedrun");

        var entries = TestJournal.Read(channel);
        Assert.Equal(Path.Combine(_workspace.DataRoot, "speedrun", "env-probe"), entries[1]);
        Assert.True(Directory.Exists(entries[1]));
        Assert.Equal("speedrun|TestHost|server", entries[2]);
    }

    [Fact]
    public void EntrypointsForOtherKindsAreIgnored() {
        var channel = TestJournal.NewChannel();
        _workspace.AddCodeMod("tool-only", "ToolOnly", RecordingMod("ToolOnly", channel), [],
            new() { ["entrypoints"] = new Dictionary<string, object> { ["imview"] = new[] { "ToolOnly.Mod" } } }, JournalReference);

        var mod = _workspace.Load().Find("tool-only")!;

        Assert.Equal(ModState.Initialized, mod.State);
        Assert.Empty(TestJournal.Read(channel));
    }

}
