using SpiralLoader.Api.Manifest;
using SpiralLoader.Runtime;
using SpiralLoader.Runtime.Manifest;

namespace SpiralLoader.Tests;

public sealed class ManifestTests {

    private const string Commit = "b07987168fcfa2d123a5d78df90ca0c76405e427";

    [Fact]
    public void ParsesCompleteManifest() {
        var result = ManifestParser.Parse($$"""
            {
              // Comments and trailing commas are allowed.
              "schemaVersion": 1,
              "id": "spiral-shuffle",
              "version": "2.1.0-beta.1",
              "name": "Spiral Shuffle",
              "description": "Randomizer",
              "authors": ["ljste"],
              "license": "AGPL-3.0-only",
              "entrypoints": { "server": "SpiralShuffle.Mod", "tool": ["SpiralShuffle.Tool"] },
              "assemblies": ["SpiralShuffle.dll", "lib/Helpers.dll"],
              "depends": { "spiralloader": ">=0.1.0", "imlight": "^1.0.0" },
              "recommends": { "enemy-inspector": "*" },
              "breaks": { "other-randomizer": "<2.0.0" },
              "loadAfter": ["some-mod"],
              "loadBefore": ["late-mod"],
              "compatibility": { "imlightCommits": ["{{Commit}}"], "clientRevisions": ["r806919.Wizard_1_610"] },
              "assets": [
                { "kind": "wad", "source": "assets/root", "target": "GameData/Root.wad" },
                { "kind": "spiraldb", "source": "spiraldb", "target": "" },
              ],
              "custom": { "anything": true },
            }
            """);

        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics));
        var manifest = result.Manifest!;
        Assert.Equal("spiral-shuffle", manifest.Id);
        Assert.Equal("2.1.0-beta.1", manifest.Version.ToString());
        Assert.Equal(["SpiralShuffle.Mod"], manifest.GetEntrypoints("server"));
        Assert.Equal(["SpiralShuffle.Tool"], manifest.GetEntrypoints("tool"));
        Assert.Equal(["imlight", "spiralloader"], manifest.Depends.Select(d => d.ModId));
        Assert.Single(manifest.Recommends);
        Assert.Single(manifest.Breaks);
        Assert.Equal([Commit], manifest.Compatibility.ImlightCommits);
        Assert.Equal(AssetKind.SpiralDb, manifest.Assets[1].Kind);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void NameDefaultsToId() {
        var result = ManifestParser.Parse("""{ "schemaVersion": 1, "id": "plain", "version": "1.0.0" }""");
        Assert.Equal("plain", result.Manifest!.Name);
    }

    [Theory]
    [InlineData("""{ "version": "1.0.0", "schemaVersion": 1 }""", "'id' is required")]
    [InlineData("""{ "id": "x1", "schemaVersion": 1 }""", "'version' is required")]
    [InlineData("""{ "id": "x1", "version": "1.0.0" }""", "'schemaVersion' is required")]
    [InlineData("""{ "id": "Bad Id", "version": "1.0.0", "schemaVersion": 1 }""", "'id'")]
    [InlineData("""{ "id": "a", "version": "1.0.0", "schemaVersion": 1 }""", "'id'")]
    [InlineData("""{ "id": "x1", "version": "1.0", "schemaVersion": 1 }""", "'version'")]
    [InlineData("""{ "id": "x1", "version": "1.0.0", "schemaVersion": 1, "entrypoints": { "server": "A.B" } }""", "'assemblies'")]
    [InlineData("""{ "id": "x1", "version": "1.0.0", "schemaVersion": 1, "assemblies": ["../escape.dll"] }""", "relative path")]
    [InlineData("""{ "id": "x1", "version": "1.0.0", "schemaVersion": 1, "assemblies": ["C:/abs.dll"] }""", "relative path")]
    [InlineData("""{ "id": "x1", "version": "1.0.0", "schemaVersion": 1, "assemblies": ["notes.txt"] }""", ".dll")]
    [InlineData("""{ "id": "x1", "version": "1.0.0", "schemaVersion": 1, "depends": { "other": ">=nope" } }""", "depends.other")]
    [InlineData("""{ "id": "x1", "version": "1.0.0", "schemaVersion": 1, "depends": { "other": 1 } }""", "depends.other")]
    [InlineData("""{ "id": "x1", "version": "1.0.0", "schemaVersion": 1, "depends": { "x1": "*" } }""", "itself")]
    [InlineData("""{ "id": "x1", "version": "1.0.0", "schemaVersion": 1, "compatibility": { "imlightCommits": ["b0798716"] } }""", "40-character")]
    [InlineData("""{ "id": "x1", "version": "1.0.0", "schemaVersion": 1, "entrypoints": { "server": [""] }, "assemblies": ["a.dll"] }""", "malformed")]
    [InlineData("""{ "id": "x1", "version": "1.0.0", "schemaVersion": 1, "assets": [{ "kind": "lua", "source": "a" }] }""", "asset kind")]
    [InlineData("""{ "id": "x1", "version": "1.0.0", "schemaVersion": 1, "assets": [{ "kind": "wad", "source": "a" }] }""", "archive")]
    public void RejectsInvalidManifests(string json, string expectedFragment) {
        var result = ManifestParser.Parse(json);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains(expectedFragment, StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsUnsafeAssetPath() {
        var result = ManifestParser.Parse("""
            { "id": "x1", "version": "1.0.0", "schemaVersion": 1,
              "assets": [{ "kind": "spiraldb", "source": "../../etc", "target": "" }] }
            """);
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.AssetInvalidPath);
    }

    [Fact]
    public void ReportsInvalidJson() {
        var result = ManifestParser.Parse("{ not json");
        Assert.Equal(DiagnosticCodes.ManifestInvalidJson, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void RejectsFutureSchemaVersion() {
        var result = ManifestParser.Parse("""{ "schemaVersion": 2, "id": "x1", "version": "1.0.0" }""");
        Assert.Contains(result.Diagnostics, d => d.Code == DiagnosticCodes.ManifestUnsupportedSchema);
    }

    [Fact]
    public void WarnsButAcceptsUnknownFields() {
        var result = ManifestParser.Parse("""{ "schemaVersion": 1, "id": "x1", "version": "1.0.0", "futureThing": 3 }""");
        Assert.True(result.Succeeded);
        Assert.Equal(DiagnosticCodes.ManifestUnknownField, Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void ReportsEveryErrorAtOnce() {
        var result = ManifestParser.Parse("""{ "schemaVersion": 1, "id": "BAD", "version": "x", "authors": [1] }""");
        Assert.True(result.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error) >= 3);
    }

}
