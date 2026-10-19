using Imcodec.MessageLayer.Generated;
using Imlight.CoreLib.Modding;
using Imlight.CoreLib.Shared.Networking;
using SpiralLoader.Api;
using SpiralLoader.Api.Events;
using SpiralLoader.Api.Logging;

namespace ImlightVerifyMod;

public sealed class VerifyMod : ISpiralMod {

    private const uint MenuJobId = 770101;
    private const uint InspectorJobId = 770102;

    public void Initialize(IModContext context) {
        context.Registries.Get(ImlightRegistries.GameMessageServices).Register("verify_service", typeof(VerifyService));

        // Spiral Shuffle's two client process job ids: claimed here so Imlight's MinigameService ignores them.
        context.Registries.Get(ImlightRegistries.ClientProcessJobs).Register("menu", MenuJobId);
        context.Registries.Get(ImlightRegistries.ClientProcessJobs).Register("inspector", InspectorJobId);

        // Behaviour is chosen by account name so each scenario in the harness exercises a different branch.
        context.Events.Subscribe<CharacterListSentEvent>(e => {
            Console.WriteLine($"VERIFY: event CharacterListSent {e.Account.Username}");
            if (e.Account.Username == "menu-user") {
                e.Send(new GAME_5_PROTOCOL.MSG_START_CLIENT_PROCESS { JobID = MenuJobId, ScriptName = "Verify/Menu.lua" });
            }
        });
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

public sealed class VerifyService(SessionActor sessionActor) : MessageService(sessionActor);
