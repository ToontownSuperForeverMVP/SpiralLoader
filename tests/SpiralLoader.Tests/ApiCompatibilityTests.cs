using System.Reflection;
using SpiralLoader.Api;
using SpiralLoader.Api.Versioning;
using SpiralLoader.Runtime;
using SpiralLoader.Tests.Infrastructure;
using SpiralLoader.Tooling;

namespace SpiralLoader.Tests;

/// <summary>
/// Guards the public compatibility contracts described in docs/api-compatibility.md against the recorded
/// baselines under api-baseline/. A failure here is a prompt to decide (and document) whether the change is
/// additive or breaking, not necessarily a bug.
/// </summary>
public sealed class ApiCompatibilityTests {

    private const string ApiBaselineFile = "SpiralLoader.Api.txt";
    private const string CodesBaselineFile = "DiagnosticCodes.txt";
    private const string UpdateHint = "If the change is intended, follow 'Updating a baseline' in docs/api-compatibility.md "
        + "(and bump the version it requires): dotnet run --project tools/SpiralLoader.ApiSurface -- dump <SpiralLoader.Api.dll>.";

    // Baseline directories are named by the release set they record, ascending.
    private static IReadOnlyList<(SemanticVersion Version, string Directory)> Baselines() => [.. Directory
        .EnumerateDirectories(BuildPaths.ApiBaselineDirectory)
        .Select(directory => (Parsed: SemanticVersion.TryParse(Path.GetFileName(directory), out var version) ? version : null, Directory: directory))
        .Where(x => x.Parsed is not null)
        .Select(x => (x.Parsed!, x.Directory))
        .OrderBy(x => x.Item1)];

    private static IReadOnlyList<string> ReadBaseline(string directory, string file)
        => [.. File.ReadAllLines(Path.Combine(directory, file)).Where(line => line.Length > 0)];

    [Fact]
    public void BaselinesExist() {
        var baselines = Baselines();
        Assert.NotEmpty(baselines);
        Assert.Contains(baselines, b => b.Version.ToString() == "1.2.0");
    }

    [Fact]
    public void ApiSurfaceMatchesTheNewestBaselineExactly() {
        var newest = Baselines()[^1];
        var comparison = new ApiComparison(ReadBaseline(newest.Directory, ApiBaselineFile), PublicApiSurface.Describe(typeof(ISpiralMod).Assembly));

        Assert.True(comparison.IsIdentical, comparison.Format($"api-baseline/{newest.Version}/{ApiBaselineFile}") + Environment.NewLine + UpdateHint);
    }

    [Fact]
    public void ApiSurfaceKeepsEverythingEveryRecordedBaselineHad() {
        var current = PublicApiSurface.Describe(typeof(ISpiralMod).Assembly);
        foreach (var (version, directory) in Baselines()) {
            var comparison = new ApiComparison(ReadBaseline(directory, ApiBaselineFile), current);
            Assert.False(comparison.IsBreaking, comparison.Format($"api-baseline/{version}/{ApiBaselineFile}") + Environment.NewLine + UpdateHint);
        }
    }

    [Fact]
    public void DiagnosticCodesNeverDisappearOrChange() {
        var current = PublicApiSurface.Describe([typeof(DiagnosticCodes)]);
        foreach (var (version, directory) in Baselines().Where(b => File.Exists(Path.Combine(b.Directory, CodesBaselineFile)))) {
            var comparison = new ApiComparison(ReadBaseline(directory, CodesBaselineFile), current);
            Assert.False(comparison.IsBreaking, comparison.Format($"api-baseline/{version}/{CodesBaselineFile}")
                + Environment.NewLine + "Diagnostic codes are stable: tooling matches on them. Add new codes instead of changing old ones.");
        }
    }

    [Fact]
    public void EveryDiagnosticCodeIsDocumentedAndEveryProblemCodeHasAdvice() {
        var architecture = File.ReadAllText(Path.Combine(BuildPaths.RepoRoot, "docs", "architecture.md"));
        var codes = typeof(DiagnosticCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(codes);
        Assert.All(codes, code => Assert.True(architecture.Contains($"`{code}`", StringComparison.Ordinal), $"{code} is missing from the diagnostics table in docs/architecture.md."));
        // mod.disabled is informational; everything else tells the reader what to do.
        Assert.All(codes.Where(c => c != DiagnosticCodes.ModDisabled),
            code => Assert.True(DiagnosticHints.Find(code) is { Length: > 20 }, $"{code} has no remediation hint in DiagnosticHints."));
        Assert.All(DiagnosticHints.Codes, code => Assert.Contains(code, codes));
    }

    [Fact]
    public void TheNewestBaselineWasRecordedFromTheApiAssemblyVersionInUse() {
        // The header names the assembly version the baseline was dumped from; the loader refuses mods built against
        // a newer one (assembly.api_newer), so a mismatch here means the version changed without a new baseline.
        var header = File.ReadLines(Path.Combine(Baselines()[^1].Directory, ApiBaselineFile)).First();
        Assert.Contains(typeof(ISpiralMod).Assembly.GetName().Version!.ToString(), header);
    }

    // The checker itself: it has to notice the changes the policy calls breaking.
    [Fact]
    public void SurfaceDiffNoticesRemovedChangedAndAddedMembers() {
        using var workspace = new TempDirectory();
        string Build(string name, string body) {
            var path = workspace.Combine(name + ".dll");
            TestCompiler.Compile(name, "namespace Sample; public class Widget { " + body + " }", path);
            return path;
        }

        var before = PublicApiSurface.Describe(Assembly.LoadFrom(Build("SurfaceBefore",
            "public int Size { get; set; } public void Resize(int size) { } public string? Label(string? text = null) => text;")));
        var after = PublicApiSurface.Describe(Assembly.LoadFrom(Build("SurfaceAfter",
            "public int Size { get; set; } public void Resize(long size) { } public string Label(string text = \"x\") => text; public void Added() { }")));

        var comparison = new ApiComparison(before, after);
        Assert.True(comparison.IsBreaking);
        Assert.Contains(comparison.Removed, l => l.Contains("Resize(System.Int32 size)"));
        Assert.Contains(comparison.Removed, l => l.Contains("Label(System.String? text = null)")); // nullability and default values are contract
        Assert.Contains(comparison.Added, l => l.Contains("Resize(System.Int64 size)"));
        Assert.Contains(comparison.Added, l => l.Contains("method System.Void Added()"));
        Assert.DoesNotContain(comparison.Removed.Concat(comparison.Added), l => l.Contains("Size {"));

        var additive = new ApiComparison(before, [.. before, "Sample.Widget :: public method System.Void Extra()"]);
        Assert.False(additive.IsBreaking);
        Assert.False(additive.IsIdentical);
    }

    [Fact]
    public void RegistryKeysAndVersionsAreRecordedByValue() {
        // Renaming a registry key is breaking even though its signature does not change.
        var surface = PublicApiSurface.Describe(typeof(ISpiralMod).Assembly);
        Assert.Contains(surface, l => l.Contains("Commands { get; } = \"spiralloader:commands\""));
        Assert.Contains(surface, l => l.Contains("ApiVersion { get; } = \"0.1.0\""));
        Assert.Contains(surface, l => l.Contains("ManifestFileName = \"spiralmod.json\""));
    }

}
