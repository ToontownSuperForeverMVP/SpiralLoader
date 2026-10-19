namespace SpiralLoader.Api.Logging;

/// <summary>Severity of a log entry.</summary>
public enum ModLogLevel {
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Fatal,
}

/// <summary>
/// A logger attributed to a single mod (or to the loader). Messages are plain text, not templates:
/// format values yourself (for example, with string interpolation).
/// </summary>
public interface IModLogger {

    /// <summary>The source the entries are attributed to (a mod id, or <c>spiralloader</c>).</summary>
    string Source { get; }

    bool IsEnabled(ModLogLevel level);

    void Log(ModLogLevel level, string message, Exception? exception = null);

}

/// <summary>Convenience helpers for <see cref="IModLogger"/>.</summary>
public static class ModLoggerExtensions {

    public static void Trace(this IModLogger logger, string message) => logger.Log(ModLogLevel.Trace, message);

    public static void Debug(this IModLogger logger, string message) => logger.Log(ModLogLevel.Debug, message);

    public static void Info(this IModLogger logger, string message) => logger.Log(ModLogLevel.Information, message);

    public static void Warn(this IModLogger logger, string message, Exception? exception = null)
        => logger.Log(ModLogLevel.Warning, message, exception);

    public static void Error(this IModLogger logger, string message, Exception? exception = null)
        => logger.Log(ModLogLevel.Error, message, exception);

}
