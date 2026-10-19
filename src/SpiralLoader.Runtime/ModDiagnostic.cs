namespace SpiralLoader.Runtime;

/// <summary>Severity of a load diagnostic.</summary>
public enum DiagnosticSeverity {
    Info,
    Warning,
    Error,
}

/// <summary>
/// A problem or note recorded while loading mods. <see cref="Code"/> is stable and documented in
/// <c>docs/architecture.md</c>; tests and tooling should match on it rather than on the message.
/// </summary>
public sealed record ModDiagnostic(DiagnosticSeverity Severity, string Code, string Message) {

    public override string ToString() => $"{Severity.ToString().ToUpperInvariant()} {Code}: {Message}";

}

/// <summary>Stable diagnostic codes.</summary>
public static class DiagnosticCodes {

    public const string ManifestUnreadable = "manifest.unreadable";
    public const string ManifestInvalidJson = "manifest.invalid_json";
    public const string ManifestInvalidField = "manifest.invalid_field";
    public const string ManifestUnknownField = "manifest.unknown_field";
    public const string ManifestUnsupportedSchema = "manifest.unsupported_schema";
    public const string ModDisabled = "mod.disabled";
    public const string ModDuplicateId = "mod.duplicate_id";
    public const string ModReservedId = "mod.reserved_id";
    public const string DependencyMissing = "dependency.missing";
    public const string DependencyVersionMismatch = "dependency.version_mismatch";
    public const string DependencyRejected = "dependency.rejected";
    public const string DependencyFailed = "dependency.failed";
    public const string DependencyCycle = "dependency.cycle";
    public const string RecommendationMissing = "recommends.missing";
    public const string RecommendationVersionMismatch = "recommends.version_mismatch";
    public const string BreaksPresent = "breaks.present";
    public const string OrderingSoftCycle = "ordering.soft_cycle";
    public const string CompatibilityImlightCommit = "compat.imlight_commit";
    public const string CompatibilityClientRevision = "compat.client_revision";
    public const string AssemblyMissing = "assembly.missing";
    public const string AssemblyLoadFailed = "assembly.load_failed";
    public const string AssemblySharedCopy = "assembly.shared_copy";
    public const string EntrypointNotFound = "entrypoint.not_found";
    public const string EntrypointInvalid = "entrypoint.invalid";
    public const string InitializationFailed = "init.failed";
    public const string AssetInvalidPath = "asset.invalid_path";
    public const string AssetMissing = "asset.missing";
    public const string AssetConflict = "asset.conflict";

}
