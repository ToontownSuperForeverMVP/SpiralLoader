using SpiralLoader.Api;
using SpiralLoader.Api.Commands;
using SpiralLoader.Api.Events;
using SpiralLoader.Api.Logging;

namespace HelloSpiral;

/// <summary>
/// Example entrypoint. Shows the three things most mods do in <see cref="Initialize"/>: subscribe to host
/// events, register contributions, and publish an API other mods can use.
/// </summary>
public sealed class HelloSpiralMod : ISpiralMod {

    /// <summary>File written to the mod's data directory when the server starts (used by integration tests).</summary>
    public const string StartedMarkerFile = "started.txt";

    private IModContext? _context;

    public void Initialize(IModContext context) {
        _context = context;
        context.Logger.Info($"Hello from {context.Manifest.Name} {context.Manifest.Version} on {context.Environment.HostName}.");

        context.Events.Subscribe<ServerStartedEvent>(OnServerStarted);
        context.Events.Subscribe<ServerStoppingEvent>(_ => context.Logger.Info("Server stopping; goodbye."));

        // Not every host exposes every registry; TryGet keeps the mod working where commands are unsupported.
        if (!context.Registries.TryGet(CommonRegistries.Commands, out var commands)) {
            context.Logger.Warn("This host has no command registry; the 'hello' command is unavailable.");
            return;
        }

        commands!.Register("hello", new ModCommand(
            "hello",
            "Replies with a greeting.",
            invocation => {
                var greeting = Greeter.Greet(invocation.Arguments.Count > 0 ? string.Join(' ', invocation.Arguments) : invocation.Caller);
                // Let other mods react to (and change) the greeting before it is returned.
                var @event = new GreetingEvent(greeting);
                context.Events.Publish(@event);
                return @event.Text;
            }) { OperatorOnly = false });
    }

    public void Shutdown() => _context?.Logger.Info("Shutdown called.");

    private void OnServerStarted(ServerStartedEvent e) {
        var marker = Path.Combine(_context!.Environment.DataDirectory, StartedMarkerFile);
        File.WriteAllText(marker, $"{e.HostName} {_context.Environment.Profile}");
        _context.Logger.Info($"Server started; wrote {marker}.");
    }

}

/// <summary>Public API other mods can call once they depend on <c>hello-spiral</c>.</summary>
public static class Greeter {

    public static string Greet(string name) => $"Hello, {name}! Welcome to the Spiral.";

}

/// <summary>Published by the <c>hello</c> command before it replies. Handlers may rewrite <see cref="Text"/>.</summary>
public sealed class GreetingEvent(string text) {

    public string Text { get; set; } = text;

}
