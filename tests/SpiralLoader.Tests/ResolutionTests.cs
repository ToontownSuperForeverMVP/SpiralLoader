using SpiralLoader.Api.Versioning;
using SpiralLoader.Runtime;
using SpiralLoader.Tests.Infrastructure;

namespace SpiralLoader.Tests;

public sealed class ResolutionTests : IDisposable {

    private readonly ModWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private static Dictionary<string, object?> Depends(params (string Id, string Range)[] dependencies)
        => new() { ["depends"] = dependencies.ToDictionary(d => d.Id, d => d.Range) };

    [Fact]
    public void OrdersDependenciesBeforeDependentsAndBreaksTiesById() {
        _workspace.AddDataMod("zeta");
        _workspace.AddDataMod("alpha", extra: Depends(("zeta", "*")));
        _workspace.AddDataMod("mid");
        _workspace.AddDataMod("beta");

        var report = _workspace.Load();

        Assert.Equal(["beta", "mid", "zeta", "alpha"], report.LoadOrder);
        Assert.All(report.Mods, m => Assert.Equal(ModState.Initialized, m.State));
    }

    [Fact]
    public void LoadOrderIsIndependentOfDirectoryNames() {
        // Directory names sort differently from ids; ordering must follow ids and dependencies only.
        _workspace.AddManifest("00-last", new { schemaVersion = 1, id = "zz-last", version = "1.0.0" });
        _workspace.AddManifest("99-first", new { schemaVersion = 1, id = "aa-first", version = "1.0.0" });

        Assert.Equal(["aa-first", "zz-last"], _workspace.Load().LoadOrder);
    }

    [Fact]
    public void HonorsLoadAfterAndLoadBefore() {
        _workspace.AddDataMod("aaa", extra: new() { ["loadAfter"] = new[] { "ccc" } });
        _workspace.AddDataMod("bbb");
        _workspace.AddDataMod("ccc", extra: new() { ["loadBefore"] = new[] { "bbb" } });
        _workspace.AddDataMod("ddd", extra: new() { ["loadAfter"] = new[] { "not-installed" } });

        Assert.Equal(["ccc", "aaa", "bbb", "ddd"], _workspace.Load().LoadOrder);
    }

    [Fact]
    public void RejectsMissingDependency() {
        _workspace.AddDataMod("needy", extra: Depends(("ghost", ">=1.0.0")));

        var mod = _workspace.Load().Find("needy")!;

        Assert.Equal(ModState.Rejected, mod.State);
        Assert.Contains(mod.Diagnostics, d => d.Code == DiagnosticCodes.DependencyMissing && d.Message.Contains("ghost"));
    }

    [Fact]
    public void RejectsDependencyVersionMismatch() {
        _workspace.AddDataMod("library", version: "1.4.0");
        _workspace.AddDataMod("consumer", extra: Depends(("library", "^2.0.0")));

        var report = _workspace.Load();

        Assert.Equal(ModState.Initialized, report.Find("library")!.State);
        var consumer = report.Find("consumer")!;
        Assert.Equal(ModState.Rejected, consumer.State);
        Assert.Contains(consumer.Diagnostics, d => d.Code == DiagnosticCodes.DependencyVersionMismatch && d.Message.Contains("1.4.0"));
    }

    [Fact]
    public void RejectionCascadesToDependents() {
        _workspace.AddDataMod("base-mod", extra: Depends(("ghost", "*")));
        _workspace.AddDataMod("middle", extra: Depends(("base-mod", "*")));
        _workspace.AddDataMod("top", extra: Depends(("middle", "*")));
        _workspace.AddDataMod("bystander");

        var report = _workspace.Load();

        Assert.Equal(["bystander"], report.LoadOrder);
        Assert.Contains(report.Find("middle")!.Diagnostics, d => d.Code == DiagnosticCodes.DependencyRejected);
        Assert.Contains(report.Find("top")!.Diagnostics, d => d.Code == DiagnosticCodes.DependencyRejected);
    }

    [Fact]
    public void RejectsHardDependencyCycleAndItsDependents() {
        _workspace.AddDataMod("cycle-a", extra: Depends(("cycle-b", "*")));
        _workspace.AddDataMod("cycle-b", extra: Depends(("cycle-c", "*")));
        _workspace.AddDataMod("cycle-c", extra: Depends(("cycle-a", "*")));
        _workspace.AddDataMod("rider", extra: Depends(("cycle-a", "*")));
        _workspace.AddDataMod("free");

        var report = _workspace.Load();

        Assert.Equal(["free"], report.LoadOrder);
        foreach (var id in new[] { "cycle-a", "cycle-b", "cycle-c" }) {
            Assert.Contains(report.Find(id)!.Diagnostics, d => d.Code == DiagnosticCodes.DependencyCycle);
        }
        Assert.Contains(report.Find("rider")!.Diagnostics, d => d.Code == DiagnosticCodes.DependencyRejected);
    }

    [Fact]
    public void BreaksSoftCyclesDeterministicallyWithWarning() {
        _workspace.AddDataMod("soft-a", extra: new() { ["loadAfter"] = new[] { "soft-b" } });
        _workspace.AddDataMod("soft-b", extra: new() { ["loadAfter"] = new[] { "soft-a" } });

        var report = _workspace.Load();

        Assert.Equal(["soft-a", "soft-b"], report.LoadOrder);
        Assert.Contains(report.Find("soft-a")!.Diagnostics, d => d.Code == DiagnosticCodes.OrderingSoftCycle);
    }

    [Fact]
    public void RejectsModThatBreaksAnInstalledMod() {
        _workspace.AddDataMod("old-randomizer", version: "1.5.0");
        _workspace.AddDataMod("new-randomizer", extra: new() { ["breaks"] = new Dictionary<string, string> { ["old-randomizer"] = "<2.0.0" } });

        var report = _workspace.Load();

        Assert.Equal(ModState.Rejected, report.Find("new-randomizer")!.State);
        Assert.Contains(report.Find("new-randomizer")!.Diagnostics, d => d.Code == DiagnosticCodes.BreaksPresent);
        Assert.Equal(ModState.Initialized, report.Find("old-randomizer")!.State);
    }

    [Fact]
    public void BreaksOutsideRangeIsAllowed() {
        _workspace.AddDataMod("old-randomizer", version: "2.0.0");
        _workspace.AddDataMod("new-randomizer", extra: new() { ["breaks"] = new Dictionary<string, string> { ["old-randomizer"] = "<2.0.0" } });

        Assert.False(_workspace.Load().HasFailures);
    }

    [Fact]
    public void RejectsDuplicateIds() {
        _workspace.AddManifest("copy-one", new { schemaVersion = 1, id = "twin", version = "1.0.0" });
        _workspace.AddManifest("copy-two", new { schemaVersion = 1, id = "twin", version = "1.0.1" });

        var report = _workspace.Load();

        Assert.Equal(2, report.Mods.Count(m => m.Id == "twin" && m.State == ModState.Rejected));
        Assert.All(report.Mods, m => Assert.Contains(m.Diagnostics, d => d.Code == DiagnosticCodes.ModDuplicateId));
    }

    [Fact]
    public void RejectsReservedBuiltInId() {
        _workspace.AddDataMod("imlight");

        var report = _workspace.Load(o => o.BuiltIns.Add(new BuiltInMod("imlight", new SemanticVersion(1, 0, 0), "test")));

        Assert.Contains(report.Find("imlight")!.Diagnostics, d => d.Code == DiagnosticCodes.ModReservedId);
    }

    [Fact]
    public void ChecksDependenciesOnHostBuiltIns() {
        _workspace.AddDataMod("needs-new-loader", extra: Depends(("spiralloader", ">=9.0.0")));
        _workspace.AddDataMod("needs-imlight", extra: Depends(("imlight", "^1.0.0")));
        _workspace.AddDataMod("needs-dotnet", extra: Depends(("dotnet", ">=10.0.0")));

        var report = _workspace.Load(o => o.BuiltIns.Add(new BuiltInMod("imlight", new SemanticVersion(1, 2, 0), "test")));

        Assert.Contains(report.Find("needs-new-loader")!.Diagnostics, d => d.Code == DiagnosticCodes.DependencyVersionMismatch);
        Assert.Equal(ModState.Initialized, report.Find("needs-imlight")!.State);
        Assert.Equal(ModState.Initialized, report.Find("needs-dotnet")!.State);
    }

    [Fact]
    public void DisabledModsAreSkippedAndTheirDependentsRejected() {
        _workspace.AddDataMod("optional-feature");
        _workspace.AddDataMod("feature-addon", extra: Depends(("optional-feature", "*")));

        var report = _workspace.Load(o => o.DisabledMods.Add("optional-feature"));

        Assert.Equal(ModState.Disabled, report.Find("optional-feature")!.State);
        Assert.False(report.Find("optional-feature")!.HasFailed);
        Assert.Equal(ModState.Rejected, report.Find("feature-addon")!.State);
    }

    [Fact]
    public void MissingRecommendationIsOnlyAWarning() {
        _workspace.AddDataMod("friendly", extra: new() { ["recommends"] = new Dictionary<string, string> { ["companion"] = "*" } });

        var mod = _workspace.Load().Find("friendly")!;

        Assert.Equal(ModState.Initialized, mod.State);
        Assert.Contains(mod.Diagnostics, d => d.Code == DiagnosticCodes.RecommendationMissing && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void RecommendedModsLoadFirst() {
        _workspace.AddDataMod("aaa", extra: new() { ["recommends"] = new Dictionary<string, string> { ["zzz"] = "*" } });
        _workspace.AddDataMod("zzz");

        Assert.Equal(["zzz", "aaa"], _workspace.Load().LoadOrder);
    }

    [Fact]
    public void InvalidManifestIsRejectedWithoutAffectingOthers() {
        _workspace.AddManifest("broken", "{ \"schemaVersion\": 1, \"id\": ");
        _workspace.AddDataMod("healthy");

        var report = _workspace.Load();

        Assert.Equal(["healthy"], report.LoadOrder);
        var broken = report.Find("broken")!;
        Assert.Equal(ModState.Rejected, broken.State);
        Assert.Contains(broken.Diagnostics, d => d.Code == DiagnosticCodes.ManifestInvalidJson);
    }

    [Fact]
    public void AbortPolicyThrowsWithFullReport() {
        _workspace.AddDataMod("needy", extra: Depends(("ghost", "*")));

        var host = _workspace.CreateHost(o => o.FailurePolicy = ModFailurePolicy.AbortStartup);
        var exception = Assert.Throws<ModLoadException>(() => host.Load());

        Assert.Contains("needy", exception.Message);
        Assert.Contains(DiagnosticCodes.DependencyMissing, exception.Message);
        Assert.True(exception.Report.HasFailures);
    }

    [Fact]
    public void MissingModsDirectoryIsNotAnError() {
        var report = _workspace.Load(o => {
            o.ModDirectories.Clear();
            o.ModDirectories.Add(Path.Combine(_workspace.ModsDirectory, "does-not-exist"));
        });

        Assert.Empty(report.Mods);
        Assert.False(report.HasFailures);
        Assert.Single(report.HostDiagnostics);
    }

    [Fact]
    public void LoadCanOnlyRunOnce() {
        var host = _workspace.CreateHost();
        host.Load();
        Assert.Throws<InvalidOperationException>(() => host.Load());
    }

    [Fact]
    public void RejectsMissingDeclaredAssembly() {
        _workspace.AddManifest("hollow", new {
            schemaVersion = 1, id = "hollow", version = "1.0.0",
            assemblies = new[] { "Hollow.dll" },
            entrypoints = new { server = "Hollow.Mod" },
        });

        var mod = _workspace.Load().Find("hollow")!;

        Assert.Equal(ModState.Rejected, mod.State);
        Assert.Contains(mod.Diagnostics, d => d.Code == DiagnosticCodes.AssemblyMissing);
    }

}
