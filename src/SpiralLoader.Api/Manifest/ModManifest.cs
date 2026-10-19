using SpiralLoader.Api.Versioning;

namespace SpiralLoader.Api.Manifest;

/// <summary>
/// A validated mod manifest (<c>spiralmod.json</c>). Instances are created by the loader's parser.
/// </summary>
public sealed class ModManifest {

    /// <summary>Manifest schema version (currently 1).</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>Unique mod id: lowercase, starts with a letter, <c>[a-z0-9_-]</c>, 2–64 characters.</summary>
    public required string Id { get; init; }

    public required SemanticVersion Version { get; init; }

    /// <summary>Display name; defaults to <see cref="Id"/>.</summary>
    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<string> Authors { get; init; } = [];

    /// <summary>SPDX license expression, if declared.</summary>
    public string? License { get; init; }

    /// <summary>
    /// Entrypoint type names by kind. The Imlight host runs <c>server</c> entrypoints; other kinds are
    /// reserved for future hosts (tools, Imview) and ignored.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Entrypoints { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Managed assemblies to load, relative to the mod directory, in load order.</summary>
    public IReadOnlyList<string> Assemblies { get; init; } = [];

    /// <summary>Required mods (or host built-ins) and acceptable versions. Missing or mismatched is an error.</summary>
    public IReadOnlyList<ModDependency> Depends { get; init; } = [];

    /// <summary>Optional mods. Missing or mismatched is a warning; when present they load first.</summary>
    public IReadOnlyList<ModDependency> Recommends { get; init; } = [];

    /// <summary>Mods this mod is incompatible with. A present mod in the range is an error.</summary>
    public IReadOnlyList<ModDependency> Breaks { get; init; } = [];

    /// <summary>Soft ordering: if present, these mods initialize before this one.</summary>
    public IReadOnlyList<string> LoadAfter { get; init; } = [];

    /// <summary>Soft ordering: if present, these mods initialize after this one.</summary>
    public IReadOnlyList<string> LoadBefore { get; init; } = [];

    /// <summary>Host and client builds this mod was built for.</summary>
    public ModCompatibility Compatibility { get; init; } = new();

    /// <summary>Declared data contributions (client archive overrides, SpiralDB overlays).</summary>
    public IReadOnlyList<AssetDeclaration> Assets { get; init; } = [];

    /// <summary>Entrypoint type names for <paramref name="kind"/>, or an empty list.</summary>
    public IReadOnlyList<string> GetEntrypoints(string kind)
        => Entrypoints.TryGetValue(kind, out var entrypoints) ? entrypoints : [];

    public override string ToString() => $"{Id} {Version}";

}

/// <summary>A dependency on another mod id with an acceptable version range.</summary>
public sealed record ModDependency(string ModId, VersionRange Range) {

    public override string ToString() => $"{ModId} {Range}";

}

/// <summary>
/// Exact builds a mod was made for. Empty lists mean "not restricted".
/// </summary>
public sealed class ModCompatibility {

    /// <summary>
    /// Imlight git commits (full 40-character SHA-1) the mod supports. Mods that rely on Imlight internals
    /// should pin commits; mods that only use public hooks should depend on <c>imlight</c> by version instead.
    /// </summary>
    public IReadOnlyList<string> ImlightCommits { get; init; } = [];

    /// <summary>Wizard101 client revisions, for example <c>r806919.Wizard_1_610</c>.</summary>
    public IReadOnlyList<string> ClientRevisions { get; init; } = [];

}

/// <summary>The kind of data a mod contributes.</summary>
public enum AssetKind {

    /// <summary>
    /// Files that override or add entries in a client KIWAD archive (Lua scripts, GUI layouts, ObjectData).
    /// </summary>
    Wad,

    /// <summary>JSON files overlaid onto the server's SpiralDB world data.</summary>
    SpiralDb,

}

/// <summary>
/// A directory of files the mod contributes. Every file under <see cref="Source"/> maps to the entry with
/// the same relative path in <see cref="Target"/>.
/// </summary>
/// <param name="Kind">What the files are applied to.</param>
/// <param name="Source">A directory relative to the mod directory.</param>
/// <param name="Target">For <see cref="AssetKind.Wad"/>, the archive path relative to the client <c>Data</c>
/// directory (for example <c>GameData/Root.wad</c>). For <see cref="AssetKind.SpiralDb"/>, the SpiralDB
/// collection directory (or empty for the root).</param>
public sealed record AssetDeclaration(AssetKind Kind, string Source, string Target);
