namespace SpiralLoader.Api;

/// <summary>
/// Entrypoint contract for a native C# mod. Implementations need a public parameterless constructor and
/// are named in the manifest's <c>entrypoints</c> section.
/// </summary>
/// <remarks>
/// Native mods are trusted code: they run in the server process with full permissions and are not sandboxed.
/// Mods are loaded once per process. Enabling, disabling, or updating a mod requires a server restart.
/// </remarks>
public interface ISpiralMod {

    /// <summary>
    /// Called once, in dependency order, after every dependency of this mod has initialized. Subscribe to
    /// events and register contributions here; registrations close once every mod has initialized.
    /// Throwing marks this mod (and every mod depending on it) as failed and rolls back its registrations.
    /// </summary>
    void Initialize(IModContext context);

    /// <summary>
    /// Called once when the host shuts down, in reverse load order. Exceptions are logged and ignored.
    /// </summary>
    void Shutdown() { }

}
