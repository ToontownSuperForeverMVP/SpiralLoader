using SpiralLoader.Api.Logging;
using SpiralLoader.Runtime;

namespace SpiralLoader.Imlight;

/// <summary>
/// The <c>[SpiralLoader]</c> section of <c>Imlight.ini</c>. Every key is optional; a missing section means
/// "enabled with defaults", which with no <c>mods</c> directory behaves exactly like unmodded Imlight.
/// </summary>
/// <remarks>
/// <code>
/// [SpiralLoader]
/// Enabled = true
/// ModsDirectory = mods
/// DataDirectory = moddata
/// ConfigDirectory = Config/mods
/// Profile = default
/// FailurePolicy = Abort            ; Abort | Continue
/// CompatibilityMode = Strict       ; Strict | WarnOnly
/// DisabledMods =                   ; comma-separated mod ids
/// LogLevel = Information           ; Trace | Debug | Information | Warning | Error
/// ImlightCommit =                  ; overrides the commit stamped at build time
/// </code>
/// Relative directories resolve against the server's base directory.
/// </remarks>
public sealed class ImlightLoaderSettings {

    public bool Enabled { get; init; } = true;

    public string ModsDirectory { get; init; } = "mods";

    public string DataDirectory { get; init; } = "moddata";

    public string ConfigDirectory { get; init; } = Path.Combine("Config", "mods");

    public string Profile { get; init; } = "default";

    public ModFailurePolicy FailurePolicy { get; init; } = ModFailurePolicy.AbortStartup;

    public CompatibilityMode CompatibilityMode { get; init; } = CompatibilityMode.Strict;

    public IReadOnlyList<string> DisabledMods { get; init; } = [];

    public ModLogLevel LogLevel { get; init; } = ModLogLevel.Information;

    public string? ImlightCommit { get; init; }

    /// <summary>Parses a configuration section. Unrecognized or malformed values fall back to defaults with a warning.</summary>
    public static ImlightLoaderSettings Parse(IReadOnlyDictionary<string, string>? section, out IReadOnlyList<string> warnings) {
        var problems = new List<string>();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in section ?? new Dictionary<string, string>()) {
            values[key.Trim()] = value.Trim();
        }

        string? Get(string key) => values.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

        T Choose<T>(string key, T fallback, Func<string, T?> parse) where T : struct {
            var text = Get(key);
            if (text is null) {
                return fallback;
            }
            var parsed = parse(text);
            if (parsed is null) {
                problems.Add($"[SpiralLoader] {key} = '{text}' is not valid; using {fallback}.");
                return fallback;
            }
            return parsed.Value;
        }

        var defaults = new ImlightLoaderSettings();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Enabled", "ModsDirectory", "DataDirectory", "ConfigDirectory", "Profile", "FailurePolicy",
            "CompatibilityMode", "DisabledMods", "LogLevel", "ImlightCommit",
        };
        foreach (var key in values.Keys.Where(k => !known.Contains(k)).Order(StringComparer.OrdinalIgnoreCase)) {
            problems.Add($"[SpiralLoader] {key} is not a known setting and was ignored.");
        }

        var settings = new ImlightLoaderSettings {
            Enabled = Choose<bool>("Enabled", defaults.Enabled, text => bool.TryParse(text, out var b) ? b : null),
            ModsDirectory = Get("ModsDirectory") ?? defaults.ModsDirectory,
            DataDirectory = Get("DataDirectory") ?? defaults.DataDirectory,
            ConfigDirectory = Get("ConfigDirectory") ?? defaults.ConfigDirectory,
            Profile = Get("Profile") ?? defaults.Profile,
            FailurePolicy = Choose("FailurePolicy", defaults.FailurePolicy, text => text.ToLowerInvariant() switch {
                "abort" or "abortstartup" => ModFailurePolicy.AbortStartup,
                "continue" or "continuewithoutfailedmods" => ModFailurePolicy.ContinueWithoutFailedMods,
                _ => (ModFailurePolicy?) null,
            }),
            CompatibilityMode = Choose("CompatibilityMode", defaults.CompatibilityMode, text => text.ToLowerInvariant() switch {
                "strict" => CompatibilityMode.Strict,
                "warnonly" or "warn" => CompatibilityMode.WarnOnly,
                _ => (CompatibilityMode?) null,
            }),
            DisabledMods = (Get("DisabledMods") ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            LogLevel = Choose("LogLevel", defaults.LogLevel,
                text => Enum.TryParse<ModLogLevel>(text, ignoreCase: true, out var level) ? level : null),
            ImlightCommit = Get("ImlightCommit")?.ToLowerInvariant(),
        };
        warnings = problems;
        return settings;
    }

}
