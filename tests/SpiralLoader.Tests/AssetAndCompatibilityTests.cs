using SpiralLoader.Api.Manifest;
using SpiralLoader.Runtime;
using SpiralLoader.Tests.Infrastructure;

namespace SpiralLoader.Tests;

public sealed class AssetAndCompatibilityTests : IDisposable {

    private const string PinnedCommit = "b07987168fcfa2d123a5d78df90ca0c76405e427";
    private const string OtherCommit = "2a16010e0000000000000000000000000000beef";

    private readonly ModWorkspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    private void AddAssetMod(string id, string file, string content, Dictionary<string, object?>? extra = null) {
        var manifest = new Dictionary<string, object?> {
            ["assets"] = new object[] { new { kind = "wad", source = "wad", target = "GameData/Root.wad" } },
        };
        foreach (var (key, value) in extra ?? []) {
            manifest[key] = value;
        }
        var directory = _workspace.AddDataMod(id, extra: manifest);
        var path = Path.Combine(directory, "wad", file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void LaterModWinsWithoutConflictWhenItOrdersItselfAfter() {
        AddAssetMod("base-ui", "GUI/Menu.gui", "base");
        AddAssetMod("ui-tweak", "GUI/Menu.gui", "tweak", new() { ["loadAfter"] = new[] { "base-ui" } });
        var host = _workspace.CreateHost();
        host.Load();

        var plan = host.PlanAssets();

        Assert.False(plan.HasConflicts);
        var entry = Assert.Single(plan.ForArchive("gamedata/root.wad"));
        Assert.Equal("ui-tweak", entry.ModId);
        Assert.Equal(["base-ui"], entry.Overrides);
        Assert.Equal("tweak", File.ReadAllText(entry.SourceFile));
    }

    [Fact]
    public void UnorderedOverlapIsAConflict() {
        AddAssetMod("mod-one", "Scripts/Shared.lua", "one");
        AddAssetMod("mod-two", "scripts/shared.lua", "two");
        AddAssetMod("mod-three", "Scripts/Unique.lua", "three");
        var host = _workspace.CreateHost();
        host.Load();

        var plan = host.PlanAssets();

        var conflict = Assert.Single(plan.Conflicts);
        Assert.Equal(("mod-one", "mod-two"), (conflict.FirstModId, conflict.SecondModId));
        Assert.Equal(2, plan.Overrides.Count);
    }

    [Fact]
    public void MissingAssetDirectoryRejectsMod() {
        _workspace.AddDataMod("no-assets", extra: new() {
            ["assets"] = new object[] { new { kind = "spiraldb", source = "spiraldb", target = "" } },
        });

        var mod = _workspace.Load().Find("no-assets")!;

        Assert.Equal(ModState.Rejected, mod.State);
        Assert.Contains(mod.Diagnostics, d => d.Code == DiagnosticCodes.AssetMissing);
    }

    private void AddPinnedMod() => _workspace.AddDataMod("pinned", extra: new() {
        ["compatibility"] = new { imlightCommits = new[] { PinnedCommit }, clientRevisions = new[] { "r806919.Wizard_1_610" } },
    });

    [Fact]
    public void StrictModeRejectsCommitMismatch() {
        AddPinnedMod();

        var mod = _workspace.Load(o => o.ImlightCommit = OtherCommit).Find("pinned")!;

        Assert.Equal(ModState.Rejected, mod.State);
        Assert.Contains(mod.Diagnostics, d => d.Code == DiagnosticCodes.CompatibilityImlightCommit && d.Message.Contains(OtherCommit));
    }

    [Fact]
    public void StrictModeRejectsClientRevisionMismatch() {
        AddPinnedMod();

        var mod = _workspace.Load(o => {
            o.ImlightCommit = PinnedCommit;
            o.ClientRevision = "r999999.Wizard_9_999";
        }).Find("pinned")!;

        Assert.Contains(mod.Diagnostics, d => d.Code == DiagnosticCodes.CompatibilityClientRevision && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void MatchingPinsLoadCleanly() {
        AddPinnedMod();

        var mod = _workspace.Load(o => {
            o.ImlightCommit = PinnedCommit.ToUpperInvariant();
            o.ClientRevision = "r806919.Wizard_1_610";
        }).Find("pinned")!;

        Assert.Equal(ModState.Initialized, mod.State);
        Assert.Empty(mod.Diagnostics);
    }

    [Fact]
    public void WarnOnlyModeLoadsWithWarning() {
        AddPinnedMod();

        var mod = _workspace.Load(o => {
            o.ImlightCommit = OtherCommit;
            o.CompatibilityMode = CompatibilityMode.WarnOnly;
        }).Find("pinned")!;

        Assert.Equal(ModState.Initialized, mod.State);
        Assert.Contains(mod.Diagnostics, d => d.Code == DiagnosticCodes.CompatibilityImlightCommit && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void UnknownHostCommitIsAWarning() {
        AddPinnedMod();

        var mod = _workspace.Load().Find("pinned")!;

        Assert.Equal(ModState.Initialized, mod.State);
        Assert.Contains(mod.Diagnostics, d => d.Code == DiagnosticCodes.CompatibilityImlightCommit && d.Message.Contains("unverified"));
    }

}
