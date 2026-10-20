namespace SpiralLoader.Runtime;

/// <summary>
/// Actionable advice for each stable diagnostic code, shown under the diagnostic in load reports. The advice is
/// written for the person who can act on it (usually the mod's author, sometimes the server operator) and never
/// replaces the specifics in <see cref="ModDiagnostic.Message"/>. Texts may be improved between releases; the
/// codes they belong to are the stable part.
/// </summary>
public static class DiagnosticHints {

    private static readonly Dictionary<string, string> s_hints = new(StringComparer.Ordinal) {
        [DiagnosticCodes.ManifestUnreadable] =
            "Make sure spiralmod.json is in the mod folder and the server process can read it.",
        [DiagnosticCodes.ManifestInvalidJson] =
            "Fix the JSON syntax: comments and trailing commas are accepted, single quotes and unquoted keys are not.",
        [DiagnosticCodes.ManifestInvalidField] =
            "Correct the field named in the message; docs/architecture.md lists every field of schema 1.",
        [DiagnosticCodes.ManifestUnknownField] =
            "The field is ignored. Remove or rename it (a typo is the usual cause); free-form data belongs under \"custom\".",
        [DiagnosticCodes.ManifestUnsupportedSchema] =
            "Set \"schemaVersion\": 1. If the mod needs a newer schema, update SpiralLoader or use a build of the mod made for this version.",
        [DiagnosticCodes.ModDuplicateId] =
            "Keep one folder per mod id: delete the extra copy (often an old version left next to the new one), or give a different mod its own id.",
        [DiagnosticCodes.ModReservedId] =
            "Choose another id; spiralloader, dotnet and imlight are provided by the host.",
        [DiagnosticCodes.DependencyMissing] =
            "Install the required mod into the mods folder, or, if the mod works without it, move it from \"depends\" to \"recommends\". For host built-ins such as imlight, run on a host that provides them (the headless host needs --imlight-api).",
        [DiagnosticCodes.DependencyVersionMismatch] =
            "Install a version of the dependency inside the required range, or widen the range in this mod's manifest after testing against that version. When the dependency is the host (spiralloader, imlight), update the host or lower the minimum.",
        [DiagnosticCodes.DependencyRejected] =
            "Fix the dependency first: its own entry in this report says why it was not loaded.",
        [DiagnosticCodes.DependencyFailed] =
            "Fix the dependency first: its own entry in this report says why it failed.",
        [DiagnosticCodes.DependencyCycle] =
            "Remove one \"depends\" entry in the cycle, or move the shared code into a third mod both can depend on.",
        [DiagnosticCodes.BreaksPresent] =
            "Remove or update the incompatible mod, or disable one of the two (Imlight.ini [SpiralLoader] DisabledMods, or --disable).",
        [DiagnosticCodes.RecommendationMissing] =
            "Optional: install the recommended mod for full functionality.",
        [DiagnosticCodes.RecommendationVersionMismatch] =
            "Optional: install a version of the recommended mod inside the range for full functionality.",
        [DiagnosticCodes.OrderingSoftCycle] =
            "Remove one of the loadAfter, loadBefore or recommends constraints in the cycle. Until then the load order follows mod ids.",
        [DiagnosticCodes.CompatibilityImlightCommit] =
            "Use a supported Imlight commit, or ask the mod author to test yours and add it to compatibility.imlightCommits. To load anyway at your own risk, set CompatibilityMode = WarnOnly ([SpiralLoader] in Imlight.ini, or --compat warn).",
        [DiagnosticCodes.CompatibilityClientRevision] =
            "Use the client revision the mod supports, or ask the mod author to test yours and add it to compatibility.clientRevisions. To load anyway at your own risk, set CompatibilityMode = WarnOnly.",
        [DiagnosticCodes.AssemblyMissing] =
            "Check that the file sits at the path listed in \"assemblies\", relative to spiralmod.json (a build that did not copy its output into the mod folder is the usual cause).",
        [DiagnosticCodes.AssemblyLoadFailed] =
            "Rebuild the mod for net10.0 and ship its private dependencies (including the .deps.json) in the mod folder. Reference SpiralLoader.Api, Imlight, Akka and Imcodec with Private=false.",
        [DiagnosticCodes.AssemblySharedCopy] =
            "Do not ship assemblies the host provides (SpiralLoader.Api, Imlight, Akka, Imcodec, the framework): reference them with Private=false and delete the copy from the mod folder.",
        [DiagnosticCodes.AssemblyApiNewer] =
            "The mod was built for a newer SpiralLoader.Api than this server provides. Update the server, or rebuild the mod against the SpiralLoader.Api this server provides.",
        [DiagnosticCodes.AssemblyApiOlder] =
            "If the mod misbehaves, rebuild it against the SpiralLoader.Api this server provides; docs/api-compatibility.md lists what changes break mods built against an older one.",
        [DiagnosticCodes.EntrypointNotFound] =
            "Check the type name in \"entrypoints.server\": it is case-sensitive, namespace-qualified (nested types use '+'), and its assembly must be listed in \"assemblies\".",
        [DiagnosticCodes.EntrypointInvalid] =
            "An entrypoint must be a public, non-abstract, non-generic class with a public parameterless constructor that implements SpiralLoader.Api.ISpiralMod.",
        [DiagnosticCodes.InitializationFailed] =
            "Fix the exception named in the message (the server log has its stack trace). Everything the mod registered was rolled back, and mods that depend on it were not loaded.",
        [DiagnosticCodes.AssetInvalidPath] =
            "Asset sources and targets must be relative paths inside the mod folder, without '..' or drive letters.",
        [DiagnosticCodes.AssetMissing] =
            "Create the declared directory inside the mod folder, or remove the \"assets\" entry.",
        [DiagnosticCodes.AssetConflict] =
            "Two mods override the same file. Make one mod load after the other (loadAfter) to let it win deliberately, or remove the overlap.",
        [DiagnosticCodes.HostModsDirectoryMissing] =
            "Create the directory, or correct the mods directory setting (Imlight.ini [SpiralLoader] ModsDirectory, or --mods).",
    };

    /// <summary>The advice for <paramref name="code"/>, or null when it has none (informational codes).</summary>
    public static string? Find(string code) => s_hints.GetValueOrDefault(code);

    /// <summary>Every code that has advice.</summary>
    public static IReadOnlyCollection<string> Codes => s_hints.Keys;

}
