using System.Collections.Concurrent;
using System.Reflection;
using SpiralLoader.Api.Commands;
using SpiralLoader.Api.Logging;
using SpiralLoader.Imlight;
using SpiralLoader.Runtime;
using SpiralLoader.Tests.Infrastructure;

namespace SpiralLoader.Tests;

public sealed class ImlightBootstrapTests {

    private const string Commit = "b07987168fcfa2d123a5d78df90ca0c76405e427";

    [Fact]
    public void MissingSectionMeansEnabledWithDefaults() {
        var settings = ImlightLoaderSettings.Parse(null, out var warnings);

        Assert.Empty(warnings);
        Assert.True(settings.Enabled);
        Assert.Equal("mods", settings.ModsDirectory);
        Assert.Equal(ModFailurePolicy.AbortStartup, settings.FailurePolicy);
        Assert.Equal(CompatibilityMode.Strict, settings.CompatibilityMode);
    }

    [Fact]
    public void ParsesEverySetting() {
        var settings = ImlightLoaderSettings.Parse(new Dictionary<string, string> {
            ["enabled"] = "False",
            ["ModsDirectory"] = "C:/Mods",
            ["DataDirectory"] = "data",
            ["ConfigDirectory"] = "cfg",
            ["Profile"] = "speedrun",
            ["FailurePolicy"] = "Continue",
            ["CompatibilityMode"] = "WarnOnly",
            ["DisabledMods"] = " a , b ,,c ",
            ["LogLevel"] = "debug",
            ["ImlightCommit"] = Commit.ToUpperInvariant(),
        }, out var warnings);

        Assert.Empty(warnings);
        Assert.False(settings.Enabled);
        Assert.Equal("C:/Mods", settings.ModsDirectory);
        Assert.Equal("speedrun", settings.Profile);
        Assert.Equal(ModFailurePolicy.ContinueWithoutFailedMods, settings.FailurePolicy);
        Assert.Equal(CompatibilityMode.WarnOnly, settings.CompatibilityMode);
        Assert.Equal(["a", "b", "c"], settings.DisabledMods);
        Assert.Equal(ModLogLevel.Debug, settings.LogLevel);
        Assert.Equal(Commit, settings.ImlightCommit);
    }

    [Fact]
    public void MalformedValuesFallBackWithWarnings() {
        var settings = ImlightLoaderSettings.Parse(new Dictionary<string, string> {
            ["Enabled"] = "maybe",
            ["FailurePolicy"] = "explode",
            ["Typo"] = "1",
        }, out var warnings);

        Assert.True(settings.Enabled);
        Assert.Equal(ModFailurePolicy.AbortStartup, settings.FailurePolicy);
        Assert.Equal(3, warnings.Count);
    }

    [Fact]
    public void DisabledLoaderReturnsNull() {
        var log = new ConcurrentQueue<string>();
        var host = ImlightModLoader.Start(new ImlightLoaderOptions {
            BaseDirectory = Path.GetTempPath(),
            Settings = new Dictionary<string, string> { ["Enabled"] = "false" },
            IntegrationApiVersion = "1.0.0",
            Log = (_, _, message, _) => log.Enqueue(message),
            RegisterProcessExit = false,
        });

        Assert.Null(host);
        Assert.Contains(log, m => m.Contains("disabled"));
    }

    [Fact]
    public void StartsWithImlightIdentityAndRelativeDirectories() {
        using var workspace = new ModWorkspace();
        workspace.AddDataMod("needs-imlight", extra: new() {
            ["depends"] = new Dictionary<string, string> { ["imlight"] = "^1.0.0" },
            ["compatibility"] = new { imlightCommits = new[] { Commit }, clientRevisions = new[] { "r806919.Wizard_1_610" } },
        });
        var baseDirectory = Path.GetDirectoryName(workspace.ModsDirectory)!;
        var entries = new ConcurrentQueue<(ModLogLevel Level, string Source, string Message)>();
        var declared = false;

        var host = ImlightModLoader.Start(new ImlightLoaderOptions {
            BaseDirectory = baseDirectory,
            Settings = new Dictionary<string, string>(),
            IntegrationApiVersion = "1.0.0",
            ImlightCommit = Commit,
            ClientRevision = " r806919.Wizard_1_610 ",
            Log = (level, source, message, _) => entries.Enqueue((level, source, message)),
            DeclareRegistries = loader => {
                declared = true;
                loader.AddRegistry(CommonRegistries.Commands, ModCommandRouter.Validate);
            },
            RegisterProcessExit = false,
        })!;

        Assert.True(declared);
        Assert.Equal(["needs-imlight"], host.LoadOrder);
        Assert.Equal("1.0.0", host.BuiltIns["imlight"].Version.ToString());
        Assert.Empty(host.Report!.Find("needs-imlight")!.Diagnostics);
        Assert.Contains(entries, e => e.Source == "spiralloader" && e.Message.Contains("host: Imlight"));
        Assert.True(host.Registries.TryGet(CommonRegistries.Commands, out _));
    }

    [Fact]
    public void AbortPolicyPropagatesLoadFailure() {
        using var workspace = new ModWorkspace();
        workspace.AddDataMod("needs-newer-imlight", extra: new() {
            ["depends"] = new Dictionary<string, string> { ["imlight"] = ">=2.0.0" },
        });

        Assert.Throws<ModLoadException>(() => ImlightModLoader.Start(new ImlightLoaderOptions {
            BaseDirectory = Path.GetDirectoryName(workspace.ModsDirectory)!,
            IntegrationApiVersion = "1.0.0",
            Log = (_, _, _, _) => { },
            RegisterProcessExit = false,
        }));
    }

    [Fact]
    public void EmptyModsDirectoryLoadsNothing() {
        using var empty = new TempDirectory();
        var host = ImlightModLoader.Start(new ImlightLoaderOptions {
            BaseDirectory = empty.Path,
            IntegrationApiVersion = "1.0.0",
            Log = (_, _, _, _) => { },
            RegisterProcessExit = false,
        })!;

        Assert.Empty(host.Report!.Mods);
        Assert.False(host.Report.HasFailures);
    }

    [Fact]
    public void ReadsOnlyWellFormedStampedCommits() {
        Assert.Null(ImlightModLoader.ReadStampedCommit(typeof(ImlightBootstrapTests).Assembly));
        Assert.Null(ImlightModLoader.ReadStampedCommit(null));
        Assert.Equal(Commit, ImlightModLoader.ReadStampedCommit(new StampedAssembly(Commit.ToUpperInvariant())));
        Assert.Null(ImlightModLoader.ReadStampedCommit(new StampedAssembly("not-a-commit")));
    }

    private sealed class StampedAssembly(string commit) : Assembly {

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
            => [new AssemblyMetadataAttribute(ImlightModLoader.CommitMetadataKey, commit)];

        public override object[] GetCustomAttributes(bool inherit) => GetCustomAttributes(typeof(object), inherit);

    }

}
