using System.Collections.Concurrent;
using SpiralLoader.Api.Logging;

namespace SpiralLoader.Runtime.Logging;

/// <summary>A single log entry produced by the loader or a mod.</summary>
public sealed record LogEntry(DateTimeOffset Timestamp, ModLogLevel Level, string Source, string Message, Exception? Exception);

/// <summary>Receives log entries. Implementations must be thread-safe.</summary>
public interface ILogSink {

    void Write(LogEntry entry);

}

/// <summary>Writes entries to standard output (errors to standard error).</summary>
public sealed class ConsoleLogSink : ILogSink {

    private readonly Lock _gate = new();

    public void Write(LogEntry entry) {
        var line = $"{entry.Timestamp:HH:mm:ss.fff} [{LevelTag(entry.Level)}] [{entry.Source}] {entry.Message}";
        if (entry.Exception is not null) {
            line += Environment.NewLine + entry.Exception;
        }

        lock (_gate) {
            var writer = entry.Level >= ModLogLevel.Error ? Console.Error : Console.Out;
            writer.WriteLine(line);
        }
    }

    internal static string LevelTag(ModLogLevel level) => level switch {
        ModLogLevel.Trace => "TRC",
        ModLogLevel.Debug => "DBG",
        ModLogLevel.Information => "INF",
        ModLogLevel.Warning => "WRN",
        ModLogLevel.Error => "ERR",
        ModLogLevel.Fatal => "FTL",
        _ => "???",
    };

}

/// <summary>Keeps entries in memory. Used by tests and diagnostics.</summary>
public sealed class MemoryLogSink : ILogSink {

    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    public void Write(LogEntry entry) => _entries.Enqueue(entry);

}

/// <summary>Forwards entries to several sinks.</summary>
public sealed class CompositeLogSink(params ILogSink[] sinks) : ILogSink {

    public void Write(LogEntry entry) {
        foreach (var sink in sinks) {
            sink.Write(entry);
        }
    }

}

/// <summary>An <see cref="IModLogger"/> that writes to an <see cref="ILogSink"/>.</summary>
internal sealed class SinkLogger(string source, ILogSink sink, ModLogLevel minimumLevel) : IModLogger {

    public string Source { get; } = source;

    public bool IsEnabled(ModLogLevel level) => level >= minimumLevel;

    public void Log(ModLogLevel level, string message, Exception? exception = null) {
        if (!IsEnabled(level)) {
            return;
        }

        try {
            sink.Write(new LogEntry(DateTimeOffset.Now, level, Source, message, exception));
        } catch {
            // A broken sink must never take the host down.
        }
    }

}
