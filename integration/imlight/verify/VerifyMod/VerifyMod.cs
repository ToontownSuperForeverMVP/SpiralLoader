using Imlight.CoreLib.Modding;
using SpiralLoader.Api;
using SpiralLoader.Api.Events;
using SpiralLoader.Api.Logging;

namespace ImlightVerifyMod;

public sealed class VerifyMod : ISpiralMod {

    public void Initialize(IModContext context) {
        // Behaviour is chosen by account name so each scenario in the harness exercises a different branch.
        context.Events.Subscribe<CharacterListSentEvent>(e => Console.WriteLine($"VERIFY: event CharacterListSent {e.Account.Username}"));
        context.Events.Subscribe<GameServerSelectingEvent>(e => {
            Console.WriteLine($"VERIFY: event GameServerSelecting {e.Account.Username} character={e.Character.CharId}");
            switch (e.Account.Username) {
                case "route-user": e.RealmName = "Shuffle"; break;
                case "unknown-realm-user": e.RealmName = "Nowhere"; break;
                case "veto-user": e.Cancel(); break;
            }
        });

        context.Events.Subscribe<ServerStartingEvent>(e => Console.WriteLine($"VERIFY: event ServerStarting {e.HostName}"));
        context.Events.Subscribe<ServerResourcesLoadedEvent>(_ => Console.WriteLine("VERIFY: event ServerResourcesLoaded"));
        context.Events.Subscribe<ServerStartedEvent>(_ => Console.WriteLine("VERIFY: event ServerStarted"));
        context.Logger.Info("verification mod initialized");
    }

}
