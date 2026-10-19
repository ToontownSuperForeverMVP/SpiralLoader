using System.Text.Json;
using System.Text.RegularExpressions;
using SpiralLoader.Api;
using SpiralLoader.Api.Manifest;
using SpiralLoader.Api.Versioning;

namespace SpiralLoader.Runtime.Manifest;

/// <summary>Result of parsing a manifest. <see cref="Manifest"/> is null when any error was found.</summary>
public sealed record ManifestParseResult(ModManifest? Manifest, IReadOnlyList<ModDiagnostic> Diagnostics) {

    public bool Succeeded => Manifest is not null;

}

/// <summary>
/// Parses and validates <c>spiralmod.json</c>. Every problem is reported (not just the first) so authors
/// can fix a manifest in one pass.
/// </summary>
public static partial class ManifestParser {

    private static readonly JsonDocumentOptions s_documentOptions = new() {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 32,
    };

    private static readonly HashSet<string> s_knownFields = [
        "$schema", "schemaVersion", "id", "version", "name", "description", "authors", "license",
        "entrypoints", "assemblies", "depends", "recommends", "breaks", "loadAfter", "loadBefore",
        "compatibility", "assets", "custom",
    ];

    /// <summary>True if <paramref name="id"/> is a valid mod id.</summary>
    public static bool IsValidModId(string? id) => id is not null && ModIdPattern().IsMatch(id);

    public static ManifestParseResult ParseFile(string path) {
        string json;
        try {
            json = File.ReadAllText(path);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            return new ManifestParseResult(null, [
                new ModDiagnostic(DiagnosticSeverity.Error, DiagnosticCodes.ManifestUnreadable, $"Cannot read {path}: {ex.Message}"),
            ]);
        }

        return Parse(json);
    }

    public static ManifestParseResult Parse(string json) {
        var diagnostics = new List<ModDiagnostic>();
        JsonDocument document;
        try {
            document = JsonDocument.Parse(json, s_documentOptions);
        } catch (JsonException ex) {
            diagnostics.Add(Error(DiagnosticCodes.ManifestInvalidJson, $"Manifest is not valid JSON: {ex.Message}"));
            return new ManifestParseResult(null, diagnostics);
        }

        using (document) {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                diagnostics.Add(Error(DiagnosticCodes.ManifestInvalidJson, "Manifest root must be a JSON object."));
                return new ManifestParseResult(null, diagnostics);
            }

            var manifest = Read(root, diagnostics);
            var hasErrors = diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
            return new ManifestParseResult(hasErrors ? null : manifest, diagnostics);
        }
    }

    private static ModManifest? Read(JsonElement root, List<ModDiagnostic> diagnostics) {
        foreach (var property in root.EnumerateObject()) {
            if (!s_knownFields.Contains(property.Name)) {
                diagnostics.Add(new ModDiagnostic(DiagnosticSeverity.Warning, DiagnosticCodes.ManifestUnknownField,
                    $"Unknown manifest field '{property.Name}' was ignored."));
            }
        }

        var schemaVersion = 0;
        if (!root.TryGetProperty("schemaVersion", out var schemaElement)) {
            diagnostics.Add(Invalid("schemaVersion", "is required."));
        } else if (schemaElement.ValueKind != JsonValueKind.Number || !schemaElement.TryGetInt32(out schemaVersion)) {
            diagnostics.Add(Invalid("schemaVersion", "must be an integer."));
        } else if (schemaVersion != SpiralLoaderInfo.ManifestSchemaVersion) {
            diagnostics.Add(Error(DiagnosticCodes.ManifestUnsupportedSchema,
                $"Manifest schemaVersion {schemaVersion} is not supported; this loader reads version {SpiralLoaderInfo.ManifestSchemaVersion}."));
        }

        var id = RequiredString(root, "id", diagnostics);
        if (id is not null && !IsValidModId(id)) {
            diagnostics.Add(Invalid("id", $"'{id}' must be 2-64 characters of lowercase letters, digits, '-' or '_', starting with a letter."));
        }

        SemanticVersion? version = null;
        var versionText = RequiredString(root, "version", diagnostics);
        if (versionText is not null && !SemanticVersion.TryParse(versionText, out version, out var versionError)) {
            diagnostics.Add(Invalid("version", versionError));
        }

        var name = OptionalString(root, "name", diagnostics);
        var description = OptionalString(root, "description", diagnostics) ?? string.Empty;
        var license = OptionalString(root, "license", diagnostics);
        var authors = StringArray(root, "authors", diagnostics, _ => null);
        var entrypoints = ReadEntrypoints(root, diagnostics);
        var assemblies = StringArray(root, "assemblies", diagnostics, value =>
            !PathRules.IsSafeRelativePath(value) ? "must be a relative path inside the mod directory."
            : !value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "must name a .dll file."
            : null);
        var duplicateAssembly = assemblies
            .GroupBy(PathRules.Normalize, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateAssembly is not null) {
            diagnostics.Add(Invalid("assemblies", $"lists '{duplicateAssembly.Key}' more than once."));
        }

        var depends = ReadDependencies(root, "depends", diagnostics);
        var recommends = ReadDependencies(root, "recommends", diagnostics);
        var breaks = ReadDependencies(root, "breaks", diagnostics);
        var loadAfter = StringArray(root, "loadAfter", diagnostics, ValidateIdReference);
        var loadBefore = StringArray(root, "loadBefore", diagnostics, ValidateIdReference);
        var compatibility = ReadCompatibility(root, diagnostics);
        var assets = ReadAssets(root, diagnostics);

        if (id is not null) {
            foreach (var dependency in depends.Concat(recommends).Concat(breaks)) {
                if (dependency.ModId == id) {
                    diagnostics.Add(Invalid("depends", $"mod '{id}' cannot reference itself."));
                }
            }
            if (loadAfter.Concat(loadBefore).Contains(id)) {
                diagnostics.Add(Invalid("loadAfter", $"mod '{id}' cannot order itself."));
            }
        }

        if (entrypoints.Values.Any(list => list.Count > 0) && assemblies.Count == 0) {
            diagnostics.Add(Invalid("assemblies", "must list at least one assembly when entrypoints are declared."));
        }

        if (id is null || version is null) {
            return null;
        }

        return new ModManifest {
            SchemaVersion = schemaVersion,
            Id = id,
            Version = version,
            Name = string.IsNullOrWhiteSpace(name) ? id : name,
            Description = description,
            Authors = authors,
            License = license,
            Entrypoints = entrypoints,
            Assemblies = assemblies,
            Depends = depends,
            Recommends = recommends,
            Breaks = breaks,
            LoadAfter = loadAfter,
            LoadBefore = loadBefore,
            Compatibility = compatibility,
            Assets = assets,
        };
    }

    private static Dictionary<string, IReadOnlyList<string>> ReadEntrypoints(JsonElement root, List<ModDiagnostic> diagnostics) {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (!root.TryGetProperty("entrypoints", out var element)) {
            return result;
        }

        if (element.ValueKind != JsonValueKind.Object) {
            diagnostics.Add(Invalid("entrypoints", "must be an object mapping entrypoint kinds to type names."));
            return result;
        }

        foreach (var kind in element.EnumerateObject()) {
            var types = new List<string>();
            var values = kind.Value.ValueKind switch {
                JsonValueKind.String => [kind.Value],
                JsonValueKind.Array => kind.Value.EnumerateArray().ToArray(),
                _ => null,
            };
            if (values is null) {
                diagnostics.Add(Invalid($"entrypoints.{kind.Name}", "must be a type name or an array of type names."));
                continue;
            }

            foreach (var value in values) {
                var typeName = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (string.IsNullOrWhiteSpace(typeName) || typeName.Any(char.IsWhiteSpace)) {
                    diagnostics.Add(Invalid($"entrypoints.{kind.Name}", "contains an empty or malformed type name."));
                    continue;
                }
                if (types.Contains(typeName)) {
                    diagnostics.Add(Invalid($"entrypoints.{kind.Name}", $"lists '{typeName}' more than once."));
                    continue;
                }
                types.Add(typeName);
            }
            result[kind.Name] = types;
        }

        return result;
    }

    private static List<ModDependency> ReadDependencies(JsonElement root, string field, List<ModDiagnostic> diagnostics) {
        var result = new List<ModDependency>();
        if (!root.TryGetProperty(field, out var element)) {
            return result;
        }

        if (element.ValueKind != JsonValueKind.Object) {
            diagnostics.Add(Invalid(field, "must be an object mapping mod ids to version ranges."));
            return result;
        }

        foreach (var property in element.EnumerateObject()) {
            if (!IsValidModId(property.Name)) {
                diagnostics.Add(Invalid(field, $"'{property.Name}' is not a valid mod id."));
                continue;
            }
            if (property.Value.ValueKind != JsonValueKind.String) {
                diagnostics.Add(Invalid($"{field}.{property.Name}", "must be a version range string (use \"*\" for any)."));
                continue;
            }
            if (!VersionRange.TryParse(property.Value.GetString(), out var range, out var error)) {
                diagnostics.Add(Invalid($"{field}.{property.Name}", error));
                continue;
            }
            result.Add(new ModDependency(property.Name, range));
        }

        return [.. result.OrderBy(d => d.ModId, StringComparer.Ordinal)];
    }

    private static ModCompatibility ReadCompatibility(JsonElement root, List<ModDiagnostic> diagnostics) {
        if (!root.TryGetProperty("compatibility", out var element)) {
            return new ModCompatibility();
        }

        if (element.ValueKind != JsonValueKind.Object) {
            diagnostics.Add(Invalid("compatibility", "must be an object."));
            return new ModCompatibility();
        }

        foreach (var property in element.EnumerateObject()) {
            if (property.Name is not ("imlightCommits" or "clientRevisions")) {
                diagnostics.Add(new ModDiagnostic(DiagnosticSeverity.Warning, DiagnosticCodes.ManifestUnknownField,
                    $"Unknown field 'compatibility.{property.Name}' was ignored."));
            }
        }

        return new ModCompatibility {
            ImlightCommits = StringArray(element, "imlightCommits", diagnostics,
                value => CommitPattern().IsMatch(value) ? null : "must contain full 40-character lowercase git commit hashes.",
                "compatibility."),
            ClientRevisions = StringArray(element, "clientRevisions", diagnostics,
                value => string.IsNullOrWhiteSpace(value) ? "must not contain empty revisions." : null,
                "compatibility."),
        };
    }

    private static List<AssetDeclaration> ReadAssets(JsonElement root, List<ModDiagnostic> diagnostics) {
        var result = new List<AssetDeclaration>();
        if (!root.TryGetProperty("assets", out var element)) {
            return result;
        }

        if (element.ValueKind != JsonValueKind.Array) {
            diagnostics.Add(Invalid("assets", "must be an array."));
            return result;
        }

        var index = 0;
        foreach (var item in element.EnumerateArray()) {
            var field = $"assets[{index++}]";
            if (item.ValueKind != JsonValueKind.Object) {
                diagnostics.Add(Invalid(field, "must be an object with kind, source and target."));
                continue;
            }

            var kindText = RequiredString(item, "kind", diagnostics, field + ".");
            var source = RequiredString(item, "source", diagnostics, field + ".");
            var target = OptionalString(item, "target", diagnostics, field + ".") ?? string.Empty;
            AssetKind? kind = kindText switch {
                "wad" => AssetKind.Wad,
                "spiraldb" => AssetKind.SpiralDb,
                null => null,
                _ => null,
            };
            if (kindText is not null && kind is null) {
                diagnostics.Add(Invalid(field + ".kind", $"'{kindText}' is not a known asset kind (expected 'wad' or 'spiraldb')."));
            }
            if (source is not null && !PathRules.IsSafeRelativePath(source)) {
                diagnostics.Add(Error(DiagnosticCodes.AssetInvalidPath, $"{field}.source '{source}' must be a relative path inside the mod directory."));
            }
            if (target.Length > 0 && !PathRules.IsSafeRelativePath(target)) {
                diagnostics.Add(Error(DiagnosticCodes.AssetInvalidPath, $"{field}.target '{target}' must be a relative path."));
            }
            if (kind == AssetKind.Wad && target.Length == 0) {
                diagnostics.Add(Invalid(field + ".target", "must name the archive for 'wad' assets (for example 'GameData/Root.wad')."));
            }
            if (kind is not null && source is not null) {
                result.Add(new AssetDeclaration(kind.Value, PathRules.Normalize(source), PathRules.Normalize(target)));
            }
        }

        return result;
    }

    private static string? ValidateIdReference(string value) => IsValidModId(value) ? null : "must contain valid mod ids.";

    private static string? RequiredString(JsonElement element, string field, List<ModDiagnostic> diagnostics, string prefix = "") {
        if (!element.TryGetProperty(field, out var value)) {
            diagnostics.Add(Invalid(prefix + field, "is required."));
            return null;
        }
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())) {
            diagnostics.Add(Invalid(prefix + field, "must be a non-empty string."));
            return null;
        }
        return value.GetString()!.Trim();
    }

    private static string? OptionalString(JsonElement element, string field, List<ModDiagnostic> diagnostics, string prefix = "") {
        if (!element.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String) {
            diagnostics.Add(Invalid(prefix + field, "must be a string."));
            return null;
        }
        return value.GetString();
    }

    private static List<string> StringArray(JsonElement element, string field, List<ModDiagnostic> diagnostics,
                                            Func<string, string?> validate, string prefix = "") {
        var result = new List<string>();
        if (!element.TryGetProperty(field, out var value)) {
            return result;
        }
        if (value.ValueKind != JsonValueKind.Array) {
            diagnostics.Add(Invalid(prefix + field, "must be an array of strings."));
            return result;
        }

        foreach (var item in value.EnumerateArray()) {
            var text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (text is null) {
                diagnostics.Add(Invalid(prefix + field, "must only contain strings."));
                continue;
            }
            var error = validate(text);
            if (error is not null) {
                diagnostics.Add(Invalid(prefix + field, $"'{text}' {error}"));
                continue;
            }
            result.Add(text);
        }
        return result;
    }

    private static ModDiagnostic Invalid(string field, string message)
        => Error(DiagnosticCodes.ManifestInvalidField, $"Manifest field '{field}' {message}");

    private static ModDiagnostic Error(string code, string message) => new(DiagnosticSeverity.Error, code, message);

    [GeneratedRegex("^[a-z][a-z0-9_-]{1,63}$")]
    private static partial Regex ModIdPattern();

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex CommitPattern();

}

/// <summary>Rules for paths that manifests may reference.</summary>
public static class PathRules {

    /// <summary>
    /// True if <paramref name="path"/> is relative, has no <c>..</c> or empty segments, and has no drive or
    /// stream separators, so it cannot escape the directory it is resolved against.
    /// </summary>
    public static bool IsSafeRelativePath(string? path) {
        if (string.IsNullOrWhiteSpace(path) || path.Contains(':') || path.Contains('\0')) {
            return false;
        }
        if (path.StartsWith('/') || path.StartsWith('\\') || Path.IsPathRooted(path)) {
            return false;
        }
        var segments = path.Split('/', '\\');
        return segments.All(segment => segment.Length > 0 && segment != ".." && segment != "." && segment.Trim() == segment);
    }

    /// <summary>Normalizes separators to '/'.</summary>
    public static string Normalize(string path) => path.Replace('\\', '/');

}
