using System.Text.RegularExpressions;
using SpiralLoader.Api;
using SpiralLoader.Api.Logging;
using SpiralLoader.Runtime.Events;
using SpiralLoader.Runtime.Logging;

namespace SpiralLoader.Runtime;

/// <summary>What the host does when any mod is rejected or fails.</summary>
public enum ModFailurePolicy {

    /// <summary>
    /// Shut down the mods that did initialize and throw <see cref="ModLoadException"/>. Default: a server
    /// running with half of its mods can corrupt saves (for example, a randomizer whose server half loaded
    /// but whose data mod did not).
    /// </summary>
    AbortStartup,

    /// <summary>Log the failures and continue with the mods that initialized.</summary>
    ContinueWithoutFailedMods,

}

/// <summary>How strictly <c>compatibility</c> pins in manifests are enforced.</summary>
public enum CompatibilityMode {

    /// <summary>A mod pinned to other Imlight commits or client revisions is rejected. Default.</summary>
    Strict,

    /// <summary>Mismatches are logged as warnings and the mod still loads.</summary>
    WarnOnly,

}

/// <summary>Configuration for a <see cref="ModHost"/>.</summary>
public sealed partial class ModHostOptions {

    /// <summary>Directories scanned for mods. Each immediate subdirectory with a manifest is one mod.</summary>
    public List<string> ModDirectories { get; } = [];

    /// <summary>Root for per-profile mod data: <c>DataRoot/profile/modid</c>.</summary>
    public string DataRoot { get; set; } = Path.Combine(AppContext.BaseDirectory, "moddata");

    /// <summary>Root for mod configuration: <c>ConfigRoot/modid</c>.</summary>
    public string ConfigRoot { get; set; } = Path.Combine(AppContext.BaseDirectory, "config", "mods");

    /// <summary>The active profile. Letters, digits, '-' and '_' only.</summary>
    public string Profile { get; set; } = "default";

    /// <summary>Shown to mods through <see cref="IModEnvironment.HostName"/>.</summary>
    public string HostName { get; set; } = "SpiralLoader";

    /// <summary>The entrypoint kind this host runs.</summary>
    public string EntrypointKind { get; set; } = SpiralLoaderInfo.ServerEntrypoint;

    public ModFailurePolicy FailurePolicy { get; set; } = ModFailurePolicy.AbortStartup;

    public HandlerFaultPolicy HandlerFaultPolicy { get; set; } = HandlerFaultPolicy.Isolate;

    public CompatibilityMode CompatibilityMode { get; set; } = CompatibilityMode.Strict;

    /// <summary>Mod ids that are present but must not load.</summary>
    public HashSet<string> DisabledMods { get; } = new(StringComparer.Ordinal);

    /// <summary>The Imlight commit the host was built from, if known (full SHA-1).</summary>
    public string? ImlightCommit { get; set; }

    /// <summary>The Wizard101 client revision the host serves, if known.</summary>
    public string? ClientRevision { get; set; }

    /// <summary>
    /// Versions provided by the host in addition to the always-present <c>spiralloader</c> and <c>dotnet</c>
    /// (for example, <c>imlight</c> with the integration API version).
    /// </summary>
    public List<BuiltInMod> BuiltIns { get; } = [];

    /// <summary>Additional assembly names that must always come from the host.</summary>
    public List<string> SharedAssemblies { get; } = [];

    public ILogSink LogSink { get; set; } = new ConsoleLogSink();

    public ModLogLevel MinimumLogLevel { get; set; } = ModLogLevel.Information;

    internal void Validate() {
        if (!ProfilePattern().IsMatch(Profile)) {
            throw new ArgumentException($"Profile '{Profile}' must be 1-32 letters, digits, '-' or '_'.");
        }
        if (string.IsNullOrWhiteSpace(EntrypointKind)) {
            throw new ArgumentException("EntrypointKind must be set.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,32}$")]
    private static partial Regex ProfilePattern();

}
