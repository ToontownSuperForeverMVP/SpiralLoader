using System.Reflection;
using SpiralLoader.Api;
using SpiralLoader.Api.Manifest;
using SpiralLoader.Api.Versioning;

namespace SpiralLoader.Runtime;

/// <summary>Where a mod is in its lifecycle.</summary>
public enum ModState {

    /// <summary>Found on disk; not yet validated.</summary>
    Discovered,

    /// <summary>Turned off by configuration. Not an error.</summary>
    Disabled,

    /// <summary>Rejected before loading: bad manifest, missing files, incompatible, or unmet dependencies.</summary>
    Rejected,

    /// <summary>Dependencies satisfied and placed in the load order.</summary>
    Resolved,

    /// <summary>Assemblies loaded and entrypoints constructed.</summary>
    Loaded,

    /// <summary>Every entrypoint's <see cref="ISpiralMod.Initialize"/> returned successfully.</summary>
    Initialized,

    /// <summary>Assembly loading, entrypoint construction, or initialization threw; registrations rolled back.</summary>
    Failed,

    /// <summary>Shutdown has run.</summary>
    ShutDown,

}

/// <summary>
/// A version provided by the host itself rather than a mod (for example <c>spiralloader</c>, <c>imlight</c>,
/// <c>dotnet</c>). Mods depend on these like any other mod id.
/// </summary>
public sealed record BuiltInMod(string Id, SemanticVersion Version, string Description);

/// <summary>Everything the loader knows about one mod directory.</summary>
public sealed class ModRecord {

    private readonly List<ModDiagnostic> _diagnostics = [];

    internal ModRecord(string directory, string manifestPath) {
        Directory = directory;
        ManifestPath = manifestPath;
    }

    /// <summary>The mod directory.</summary>
    public string Directory { get; }

    public string ManifestPath { get; }

    /// <summary>The manifest, or null if it could not be parsed.</summary>
    public ModManifest? Manifest { get; internal set; }

    /// <summary>The manifest id, or the directory name if the manifest could not be parsed.</summary>
    public string Id => Manifest?.Id ?? Path.GetFileName(Directory);

    public ModState State { get; internal set; } = ModState.Discovered;

    /// <summary>Position in the initialization order, or -1 if the mod never reached it.</summary>
    public int LoadIndex { get; internal set; } = -1;

    public IReadOnlyList<ModDiagnostic> Diagnostics => _diagnostics;

    public IReadOnlyList<Assembly> Assemblies { get; internal set; } = [];

    internal List<ISpiralMod> Entrypoints { get; } = [];

    internal ModLoadContext? LoadContext { get; set; }

    /// <summary>True for states that count as a load problem (not <see cref="ModState.Disabled"/>).</summary>
    public bool HasFailed => State is ModState.Rejected or ModState.Failed;

    public bool HasErrors => _diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    internal void Add(ModDiagnostic diagnostic) => _diagnostics.Add(diagnostic);

    internal void AddRange(IEnumerable<ModDiagnostic> diagnostics) => _diagnostics.AddRange(diagnostics);

    internal void Reject(string code, string message) {
        _diagnostics.Add(new ModDiagnostic(DiagnosticSeverity.Error, code, message));
        State = ModState.Rejected;
    }

    internal void Fail(string code, string message) {
        _diagnostics.Add(new ModDiagnostic(DiagnosticSeverity.Error, code, message));
        State = ModState.Failed;
    }

    internal void Warn(string code, string message)
        => _diagnostics.Add(new ModDiagnostic(DiagnosticSeverity.Warning, code, message));

    public override string ToString() => $"{Id} [{State}]";

}
