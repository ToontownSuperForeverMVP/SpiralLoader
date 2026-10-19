namespace SpiralLoader.Api.Events;

/// <summary>
/// Published once after every mod has initialized and registrations have closed.
/// </summary>
public sealed class ModsLoadedEvent(IReadOnlyList<string> loadOrder) {

    /// <summary>Ids of the initialized mods in load order.</summary>
    public IReadOnlyList<string> LoadOrder { get; } = loadOrder;

}

/// <summary>
/// Published by the host before it loads world data and opens network listeners.
/// </summary>
public sealed class ServerStartingEvent(string hostName) {

    public string HostName { get; } = hostName;

}

/// <summary>
/// Published by the host after world data has loaded, before clients can connect.
/// </summary>
public sealed class ServerResourcesLoadedEvent(string hostName) {

    public string HostName { get; } = hostName;

}

/// <summary>
/// Published by the host once it accepts client connections.
/// </summary>
public sealed class ServerStartedEvent(string hostName) {

    public string HostName { get; } = hostName;

}

/// <summary>
/// Published by the host when the process is shutting down, before mod <c>Shutdown</c> calls.
/// Handlers must finish quickly: the process may be terminated shortly after.
/// </summary>
public sealed class ServerStoppingEvent(string hostName) {

    public string HostName { get; } = hostName;

}
