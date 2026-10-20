using SpiralLoader.Api;
using SpiralLoader.Api.Commands;
using SpiralLoader.Api.Events;
using SpiralLoader.Api.Logging;

namespace StandaloneMod;

/// <summary>Named in spiralmod.json. One instance is created when the server starts.</summary>
public sealed class StandaloneEntry : ISpiralMod {

    public void Initialize(IModContext context) {
        context.Logger.Info($"{context.Manifest.Name} {context.Manifest.Version} loading on {context.Environment.HostName}.");

        // Subscribe and register only while Initialize runs; registrations close afterwards.
        context.Events.Subscribe<ServerStartedEvent>(e => context.Logger.Info($"{e.HostName} is up."));

        context.Registries.Get(CommonRegistries.Commands).Register("standalone",
            new ModCommand("standalone", "Replies from the standalone example mod.",
                invocation => $"standalone-mod says hello to {(invocation.Arguments.Count > 0 ? invocation.Arguments[0] : invocation.Caller)}.") {
                OperatorOnly = false,
            });
    }

    public void Shutdown() { }

}
