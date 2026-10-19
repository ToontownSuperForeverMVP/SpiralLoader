using System.Reflection;
using SpiralLoader.Api.Events;
using SpiralLoader.Api.Logging;
using SpiralLoader.Api.Versioning;
using SpiralLoader.Runtime;
using SpiralLoader.Runtime.Logging;

namespace SpiralLoader.Imlight;

/// <summary>Writes a SpiralLoader log entry through Imlight's logger.</summary>
public delegate void ImlightLogWriter(ModLogLevel level, string source, string message, Exception? exception);

/// <summary>What Imlight's Director passes to <see cref="ImlightModLoader.Start"/>.</summary>
public sealed class ImlightLoaderOptions {

    /// <summary>The server's base directory (<c>AppDomain.CurrentDomain.BaseDirectory</c>).</summary>
    public required string BaseDirectory { get; init; }

    /// <summary>The <c>[SpiralLoader]</c> section of <c>Imlight.ini</c> (empty or null if absent).</summary>
    public IReadOnlyDictionary<string, string>? Settings { get; init; }

    /// <summary>The version of Imlight's integration surface, exposed to mods as the <c>imlight</c> built-in.</summary>
    public required string IntegrationApiVersion { get; init; }

    /// <summary>The Imlight commit stamped at build time, if any (see <see cref="ImlightModLoader.ReadStampedCommit"/>).</summary>
    public string? ImlightCommit { get; init; }

    /// <summary>The <c>GameRevision</c> Imlight serves (from <c>[Global Settings]</c>).</summary>
    public string? ClientRevision { get; init; }

    /// <summary>Destination for loader and mod log entries.</summary>
    public required ImlightLogWriter Log { get; init; }

    /// <summary>Declares the registries Imlight consumes. Called before mods load.</summary>
    public Action<ModHost>? DeclareRegistries { get; init; }

    /// <summary>Publish <see cref="ServerStoppingEvent"/> and shut mods down on process exit. Default true.</summary>
    public bool RegisterProcessExit { get; init; } = true;

}

/// <summary>
/// Starts SpiralLoader inside Imlight. Call once from the Director, after configuration is loaded and before
/// the actor system, resources, or any server is created.
/// </summary>
public static class ImlightModLoader {

    public const string HostName = "Imlight";

    /// <summary>The assembly metadata key the Imlight build stamps its git commit under.</summary>
    public const string CommitMetadataKey = "ImlightCommit";

    /// <summary>
    /// Loads mods. Returns null when <c>[SpiralLoader] Enabled = false</c>. Throws <see cref="ModLoadException"/>
    /// when a mod fails under <c>FailurePolicy = Abort</c>; the report has already been logged.
    /// </summary>
    public static ModHost? Start(ImlightLoaderOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        var sink = new DelegateLogSink(options.Log);
        var settings = ImlightLoaderSettings.Parse(options.Settings, out var warnings);
        foreach (var warning in warnings) {
            sink.Write(new LogEntry(DateTimeOffset.Now, ModLogLevel.Warning, "spiralloader", warning, null));
        }

        if (!settings.Enabled) {
            sink.Write(new LogEntry(DateTimeOffset.Now, ModLogLevel.Information, "spiralloader",
                "SpiralLoader is disabled ([SpiralLoader] Enabled = false); Imlight runs without mods.", null));
            return null;
        }

        var hostOptions = CreateHostOptions(options, settings, sink);
        var host = new ModHost(hostOptions);
        options.DeclareRegistries?.Invoke(host);
        host.Load();

        if (options.RegisterProcessExit) {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => {
                host.Events.Publish(new ServerStoppingEvent(HostName));
                host.Shutdown();
            };
        }
        return host;
    }

    /// <summary>Reads the commit the Imlight build stamped into <paramref name="assembly"/>, if any.</summary>
    public static string? ReadStampedCommit(Assembly? assembly) {
        var value = assembly?.GetCustomAttributes(typeof(AssemblyMetadataAttribute), inherit: false)
            .OfType<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == CommitMetadataKey)?.Value?.Trim().ToLowerInvariant();
        return value is { Length: 40 } && value.All(char.IsAsciiHexDigitLower) ? value : null;
    }

    internal static ModHostOptions CreateHostOptions(ImlightLoaderOptions options, ImlightLoaderSettings settings, ILogSink sink) {
        if (!SemanticVersion.TryParse(options.IntegrationApiVersion, out var integrationVersion)) {
            throw new ArgumentException($"Integration API version '{options.IntegrationApiVersion}' is not a semantic version.");
        }

        var hostOptions = new ModHostOptions {
            HostName = HostName,
            Profile = settings.Profile,
            DataRoot = Resolve(options.BaseDirectory, settings.DataDirectory),
            ConfigRoot = Resolve(options.BaseDirectory, settings.ConfigDirectory),
            FailurePolicy = settings.FailurePolicy,
            CompatibilityMode = settings.CompatibilityMode,
            ImlightCommit = settings.ImlightCommit ?? options.ImlightCommit,
            ClientRevision = string.IsNullOrWhiteSpace(options.ClientRevision) ? null : options.ClientRevision.Trim(),
            LogSink = sink,
            MinimumLogLevel = settings.LogLevel,
        };
        hostOptions.ModDirectories.Add(Resolve(options.BaseDirectory, settings.ModsDirectory));
        hostOptions.BuiltIns.Add(new BuiltInMod("imlight", integrationVersion, "Imlight integration API"));
        foreach (var id in settings.DisabledMods) {
            hostOptions.DisabledMods.Add(id);
        }
        return hostOptions;
    }

    private static string Resolve(string baseDirectory, string path)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));

    private sealed class DelegateLogSink(ImlightLogWriter writer) : ILogSink {

        public void Write(LogEntry entry) => writer(entry.Level, entry.Source, entry.Message, entry.Exception);

    }

}
