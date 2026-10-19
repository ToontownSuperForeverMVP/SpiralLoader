using SpiralLoader.Api.Versioning;

namespace SpiralLoader.Api;

/// <summary>
/// Identity of the SpiralLoader API that a mod is compiled against and running under.
/// </summary>
public static class SpiralLoaderInfo {

    /// <summary>The mod id under which the loader itself is exposed to dependency resolution.</summary>
    public const string LoaderModId = "spiralloader";

    /// <summary>The manifest schema version this loader reads.</summary>
    public const int ManifestSchemaVersion = 1;

    /// <summary>The file name of a mod manifest inside a mod directory.</summary>
    public const string ManifestFileName = "spiralmod.json";

    /// <summary>The entrypoint kind run inside the game server process.</summary>
    public const string ServerEntrypoint = "server";

    /// <summary>The semantic version of the public mod API.</summary>
    public static SemanticVersion ApiVersion { get; } = new(0, 1, 0);

}
