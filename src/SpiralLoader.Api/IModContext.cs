using SpiralLoader.Api.Events;
using SpiralLoader.Api.Logging;
using SpiralLoader.Api.Manifest;
using SpiralLoader.Api.Registries;

namespace SpiralLoader.Api;

/// <summary>
/// Everything a mod receives from the host during <see cref="ISpiralMod.Initialize"/>.
/// </summary>
public interface IModContext {

    /// <summary>The parsed manifest of this mod.</summary>
    ModManifest Manifest { get; }

    /// <summary>A logger whose entries are attributed to this mod.</summary>
    IModLogger Logger { get; }

    /// <summary>The event bus. Subscriptions made through it are owned by this mod.</summary>
    IEventBus Events { get; }

    /// <summary>Registries the host exposes for contributions (for example, game message services).</summary>
    IRegistryProvider Registries { get; }

    /// <summary>Directories and host information for this mod.</summary>
    IModEnvironment Environment { get; }

    /// <summary>Read-only view of the other mods in this process.</summary>
    IModQuery Mods { get; }

}

/// <summary>
/// Host and filesystem information available to a mod.
/// </summary>
public interface IModEnvironment {

    /// <summary>The directory the mod was loaded from (contains its manifest). Treat as read-only.</summary>
    string ModDirectory { get; }

    /// <summary>A writable directory for this mod's persistent data, scoped to the active profile.</summary>
    string DataDirectory { get; }

    /// <summary>A directory for this mod's user-editable configuration files.</summary>
    string ConfigDirectory { get; }

    /// <summary>The active profile name (for example, <c>default</c>).</summary>
    string Profile { get; }

    /// <summary>The name of the host (for example, <c>Imlight</c> or <c>SpiralLoader.Host</c>).</summary>
    string HostName { get; }

    /// <summary>The entrypoint kind the host runs (for example, <c>server</c>).</summary>
    string EntrypointKind { get; }

}

/// <summary>
/// Read-only information about the mods in the current process.
/// </summary>
public interface IModQuery {

    /// <summary>True if a mod or host built-in with this id is present and has not failed.</summary>
    bool IsLoaded(string modId);

    /// <summary>Gets the manifest of a loaded mod, or null.</summary>
    ModManifest? GetManifest(string modId);

    /// <summary>Ids of loaded mods in load order (host built-ins excluded).</summary>
    IReadOnlyList<string> LoadOrder { get; }

}
