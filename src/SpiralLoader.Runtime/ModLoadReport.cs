using System.Text;

namespace SpiralLoader.Runtime;

/// <summary>The outcome of <see cref="ModHost.Load"/>.</summary>
public sealed class ModLoadReport {

    internal ModLoadReport(IReadOnlyList<ModRecord> mods, IReadOnlyList<ModDiagnostic> hostDiagnostics) {
        Mods = mods;
        HostDiagnostics = hostDiagnostics;
    }

    /// <summary>Every discovered mod: those that reached the load order first (in order), then the rest by id.</summary>
    public IReadOnlyList<ModRecord> Mods { get; }

    /// <summary>Diagnostics not tied to one mod (for example, a missing mods directory).</summary>
    public IReadOnlyList<ModDiagnostic> HostDiagnostics { get; }

    /// <summary>Ids of initialized mods in load order.</summary>
    public IReadOnlyList<string> LoadOrder => [.. Mods.Where(m => m.State is ModState.Initialized or ModState.ShutDown).Select(m => m.Id)];

    public IEnumerable<ModRecord> Failures => Mods.Where(m => m.HasFailed);

    /// <summary>True if any mod was rejected or failed, or the host itself reported an error (for example a requested mod folder without a manifest).</summary>
    public bool HasFailures => Mods.Any(m => m.HasFailed) || HostErrors.Any();

    /// <summary>Error-level <see cref="HostDiagnostics"/>.</summary>
    public IEnumerable<ModDiagnostic> HostErrors => HostDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);

    public ModRecord? Find(string id) => Mods.FirstOrDefault(m => m.Id == id);

    /// <summary>A human-readable multi-line summary.</summary>
    public string Format() {
        var builder = new StringBuilder();
        var initialized = Mods.Count(m => m.State == ModState.Initialized);
        builder.AppendLine($"SpiralLoader: {Mods.Count} mod(s) found, {initialized} initialized, {Failures.Count()} failed.");
        if (HasFailures) {
            builder.AppendLine("  Needs attention: " + string.Join("; ", Failures.Select(Summarize).Concat(HostErrors.Select(e => $"host [{e.Code}]"))));
        }
        foreach (var diagnostic in HostDiagnostics) {
            AppendDiagnostic(builder, "  ", diagnostic);
        }
        foreach (var mod in Mods) {
            var version = mod.Manifest?.Version.ToString() ?? "?";
            var index = mod.LoadIndex >= 0 ? $"#{mod.LoadIndex + 1}" : "--";
            builder.AppendLine($"  {index,-4} {mod.Id} {version} [{mod.State}] ({mod.Directory})");
            foreach (var diagnostic in mod.Diagnostics) {
                AppendDiagnostic(builder, "         ", diagnostic);
            }
        }
        return builder.ToString().TrimEnd();
    }

    // "bad-mod 1.0.0 [Failed: init.failed]" - which mod, in what state, and the code to look up.
    private static string Summarize(ModRecord mod) {
        var codes = mod.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Code).Distinct();
        var version = mod.Manifest?.Version.ToString();
        return $"{mod.Id}{(version is null ? string.Empty : " " + version)} [{mod.State}: {string.Join(", ", codes)}]";
    }

    private static void AppendDiagnostic(StringBuilder builder, string indent, ModDiagnostic diagnostic) {
        builder.AppendLine($"{indent}{diagnostic}");
        if (diagnostic.Severity != DiagnosticSeverity.Info && diagnostic.Hint is { } hint) {
            builder.AppendLine($"{indent}     fix: {hint}");
        }
    }

}

/// <summary>
/// Thrown by <see cref="ModHost.Load"/> under <see cref="ModFailurePolicy.AbortStartup"/> when any mod was
/// rejected or failed.
/// </summary>
public sealed class ModLoadException(ModLoadReport report)
    : Exception("One or more mods failed to load:" + Environment.NewLine + report.Format()) {

    public ModLoadReport Report { get; } = report;

}
