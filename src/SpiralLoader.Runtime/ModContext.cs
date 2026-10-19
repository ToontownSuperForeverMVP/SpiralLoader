using SpiralLoader.Api;
using SpiralLoader.Api.Events;
using SpiralLoader.Api.Logging;
using SpiralLoader.Api.Manifest;
using SpiralLoader.Api.Registries;

namespace SpiralLoader.Runtime;

internal sealed class ModContext(
    ModManifest manifest,
    IModLogger logger,
    IEventBus events,
    IRegistryProvider registries,
    IModEnvironment environment,
    IModQuery mods) : IModContext {

    public ModManifest Manifest { get; } = manifest;

    public IModLogger Logger { get; } = logger;

    public IEventBus Events { get; } = events;

    public IRegistryProvider Registries { get; } = registries;

    public IModEnvironment Environment { get; } = environment;

    public IModQuery Mods { get; } = mods;

}

/// <summary>Directories are created on first access so mods that never use them leave nothing behind.</summary>
internal sealed class ModEnvironment(
    string modDirectory,
    string dataDirectory,
    string configDirectory,
    string profile,
    string hostName,
    string entrypointKind) : IModEnvironment {

    public string ModDirectory { get; } = modDirectory;

    public string DataDirectory => Directory.CreateDirectory(dataDirectory).FullName;

    public string ConfigDirectory => Directory.CreateDirectory(configDirectory).FullName;

    public string Profile { get; } = profile;

    public string HostName { get; } = hostName;

    public string EntrypointKind { get; } = entrypointKind;

}
