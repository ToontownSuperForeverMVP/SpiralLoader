using Imlight.CoreLib.Modding;
using Imlight.CoreLib.Shared.Networking;
using SpiralLoader.Api;
using SpiralLoader.Api.Events;
using SpiralLoader.Api.Logging;

namespace ImlightVerifyMod;

public sealed class VerifyMod : ISpiralMod {

    public void Initialize(IModContext context) {
        context.Registries.Get(ImlightRegistries.GameMessageServices).Register("verify_service", typeof(VerifyService));
        context.Events.Subscribe<ServerStartingEvent>(e => Console.WriteLine($"VERIFY: event ServerStarting {e.HostName}"));
        context.Events.Subscribe<ServerResourcesLoadedEvent>(_ => Console.WriteLine("VERIFY: event ServerResourcesLoaded"));
        context.Events.Subscribe<ServerStartedEvent>(_ => Console.WriteLine("VERIFY: event ServerStarted"));
        context.Logger.Info("verification mod initialized");
    }

}

public sealed class VerifyService(SessionActor sessionActor) : MessageService(sessionActor);
