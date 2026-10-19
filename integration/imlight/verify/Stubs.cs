// Functional stubs for the Imlight types the harness cannot compile (they depend on Imcodec generated code).
// Signatures follow the pinned Imlight source; behaviour is the minimum Program.Main needs to run through.
// Lines starting with "VERIFY:" are asserted by verify-patch.sh.
using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Imlight.CoreLib.Game;
using Imlight.CoreLib.Login;
using Imcodec.MessageLayer;
using Imcodec.MessageLayer.Generated;
using Imlight.CoreLib.Shared.Packets;

namespace Imcodec.ObjectProperty.TypeCache {
    public class CoreTemplate { }

    // Simulates a generated type named 'Type' (Imlight aliases System.Type for this reason).
    public class Type { }
}

namespace Imlight.CoreLib.Shared.Networking {
    // The real SessionActor owns a socket. This one only records what services send to "the client" and routes
    // server asks to ServerRef, which is all the services under test use.
    public sealed class SessionActor {
        public IActorRef ActorRef { get; init; }
        public IActorRef ServerRef { get; init; }
        public int SessionID { get; init; } = 1;
        public IMessage CachedDequeueMessage { get; set; }

        public IMessage EnqueueToServer(IActorRef gameServer) => new LOGIN_7_PROTOCOL.MSG_CHARACTERSELECTED { PrepPhase = 0 };

        public T AskServer<T>(IServerMessage message) where T : IServerMessage => ServerRef.Ask<T>(message).Result;
    }

    public sealed class SessionFatalException(string message) : Exception(message);

    // Internal in Imlight. Public here only so the verification mod can derive from it without a publicizer.
    // Derives from Imlight's real ReceiveProtocolDispatcher, so [MessageHandler] dispatch is the real thing.
    public abstract class MessageService(SessionActor sessionActor) : ReceiveProtocolDispatcher {
        protected SessionActor SessionActor { get; } = sessionActor;

        protected void SendToSocket(IMessage message) => SessionActor.ActorRef.Tell(message);

        protected T AskServer<T>(IServerMessage message) where T : IServerMessage => SessionActor.AskServer<T>(message);

        protected Imlight.CoreLib.WizardData.Models.Player.Account GetSocketAccount() => ScenarioWorld.AccountOf(SessionActor);

        protected void CloseSession() => SessionActor.ActorRef.Tell("Close");

        protected Imlight.CoreLib.WizardData.Models.Player.Wizard GetActiveWizard() => throw new NotSupportedException("not used by the scenarios");

        protected void Teleport(string destinationZone, string destinationLocation, bool makePrivate) => throw new NotSupportedException("not used by the scenarios");
    }

    public static class Server {
        public static readonly TimeSpan StartListeningTimeout = TimeSpan.FromSeconds(30);
    }
}

namespace Imlight.CoreLib.Shared.Services {
    using Imlight.CoreLib.Shared.Networking;
    internal sealed class ControlService(SessionActor s) : MessageService(s) { }
    internal sealed class AccountService(SessionActor s) : MessageService(s) { }
}

namespace Imlight.CoreLib.Game.Services {
    using Imlight.CoreLib.Shared.Networking;
    internal sealed class AttachService(SessionActor s) : MessageService(s) { }
    internal sealed class ClientService(SessionActor s) : MessageService(s) { }
    internal sealed class MoveService(SessionActor s) : MessageService(s) { }
    internal sealed class ZoneService(SessionActor s) : MessageService(s) { }
    internal sealed class WizardService(SessionActor s) : MessageService(s) { }
    internal sealed class ChatService(SessionActor s) : MessageService(s) { }
    internal sealed class SpellbookService(SessionActor s) : MessageService(s) { }
    internal sealed class InventoryService(SessionActor s) : MessageService(s) { }
    internal sealed class EquipmentService(SessionActor s) : MessageService(s) { }
    internal sealed class CommandService(SessionActor s) : MessageService(s) { }
    internal sealed class CombatService(SessionActor s) : MessageService(s) { }
    internal sealed class InteractService(SessionActor s) : MessageService(s) { }
    internal sealed class ShopService(SessionActor s) : MessageService(s) { }
    internal sealed class AuctionHouseService(SessionActor s) : MessageService(s) { }
    internal sealed class DynaModService(SessionActor s) : MessageService(s) { }
    internal sealed class CantripService(SessionActor s) : MessageService(s) { }
    internal sealed class TrainService(SessionActor s) : MessageService(s) { }
    internal sealed class PetService(SessionActor s) : MessageService(s) { }
    internal sealed class FriendsService(SessionActor s) : MessageService(s) { }
    internal sealed class GroupService(SessionActor s) : MessageService(s) { }
    internal sealed class TutorialService(SessionActor s) : MessageService(s) { }
    internal sealed class QuestService(SessionActor s) : MessageService(s) { }
    internal sealed class CinematicService(SessionActor s) : MessageService(s) { }
    internal sealed class PotionService(SessionActor s) : MessageService(s) { }
    internal sealed class TreasureShopService(SessionActor s) : MessageService(s) { }
}

namespace Imlight.CoreLib.Login.Services {
    using Imlight.CoreLib.Shared.Networking;
    internal sealed class AuthenticatorService(SessionActor s) : MessageService(s) { }
    internal sealed class LoginAFKService(SessionActor s) : MessageService(s) { }
}

namespace Imlight.CoreLib.Shared.Resources {
    public abstract class RootSingleResourceSingleton<T> where T : RootSingleResourceSingleton<T>, new() { }
    public abstract class RootDirectoryResourceSingleton<T> where T : RootDirectoryResourceSingleton<T>, new() { }
}

namespace Imlight.CoreLib.Game.Zone.Core {
    public class ZoneEntity { }
    public interface IZoneComponent { }
    public abstract class ZoneEntityComponent(ZoneEntity entity) : IZoneComponent {
        protected ZoneEntity Entity { get; } = entity;
    }
}

namespace Imlight.CoreLib.Auth {
    public static class LoginWarmup {
        public static void Run() { }
    }
}

namespace Imlight.CoreLib.Patch {
    public static class PatchServer {
        public static Props Props(string name, ushort port) => Akka.Actor.Props.Create(() => new StubPatchServer());

        private sealed class StubPatchServer : ReceiveActor {
            public StubPatchServer()
                => Receive<SERVER_100_PROTOCOL.MSG_INITIALIZE>(_ => Sender.Tell(new SERVER_100_PROTOCOL.MSG_INITIALIZE_COMPLETE()));
        }
    }
}

namespace Imlight.CoreLib.Login {
    public static class LoginServer {
        public static Props Props(string name, ushort port) => Akka.Actor.Props.Create(() => new StubLoginServer());

        // Creates the real (patched) service factories and reports the services a session would get.
        private sealed class StubLoginServer : ReceiveActor {
            public StubLoginServer() {
                var login = Context.ActorOf(LoginServiceFactory.Props(), "loginServiceFactory");
                Report("login", login);
                HookScenarios.Run(Context.System);
                Receive<SERVER_100_PROTOCOL.MSG_CREATEGAMESERVER>(message => {
                    var game = Context.ActorOf(GameServiceFactory.Props(), $"gameServiceFactory-{message.Port}");
                    Report($"game {message.RealmName}", game);
                });
                // MSG_STARTLISTENING exists only in newer Imlight (the quality-assurance Director sends it), so match by name.
                ReceiveAny(message => {
                    if (message.GetType().Name == "MSG_STARTLISTENING") {
                        Sender.Tell(Activator.CreateInstance(typeof(SERVER_100_PROTOCOL).GetNestedType("MSG_STARTLISTENING_COMPLETE")));
                    }
                });
            }

            private static void Report(string label, IActorRef factory) {
                var services = factory.Ask<SERVICE_101_PROTOCOL.MSG_SERVICESLIST>(
                    new SERVICE_101_PROTOCOL.MSG_QUERYUNLOADEDSERVICES(), TimeSpan.FromSeconds(10)).Result.Services;
                Console.WriteLine($"VERIFY: {label} services={services.Count} mod=[{string.Join(",", services.Where(t => !t.Assembly.Equals(typeof(LoginServer).Assembly)).Select(t => t.FullName))}]");
            }
        }
    }
}

namespace Imlight.CoreLib.WizardData {
    public static class SpiralDB {
        public static void Load() => Console.WriteLine("VERIFY: SpiralDB.Load");
    }
}

namespace Imlight.CoreLib.WizardData.Databases {
    public sealed class PlayerDatabase {
        public static PlayerDatabase Instance { get; } = new();
        public object Store => this;
    }

    public static class DatabaseUtilities {
        public static void CreateEmbeddedDatabaseAccount(string username, string email, string password,
                                                         Imlight.CoreLib.WizardData.Models.Player.AuthLevel level) { }
    }
}

namespace Imlight.CoreLib.WizardData.Collections {
    public static class OnlinePlayerCollection {
        public static void Clear() { }
    }
}

namespace Imlight.CoreLib.WizardData.Models.Player {
    public enum AuthLevel { None, QualityAssurance, HallMonitor, Developer, Administrator }

    public class Account {
        public Account() { }
        public Account(string username, ulong accountId) { Username = username; AccountId = accountId; }
        public string Username { get; private set; }
        public ulong AccountId { get; private set; }
        public AuthLevel AuthLevel { get; set; }
        public int PurchasedCharacterSlots { get; set; } = 2;
        public List<Wizard> Characters = new();
        public bool AddCharacter(Wizard character) { Characters.Add(character); return true; }
        public bool DeleteCharacter(ulong id) => Characters.RemoveAll(c => c.CharId == id) > 0;
        public Wizard GetCharacter(ulong id) => Characters.FirstOrDefault(c => c.CharId == id);
    }

    public class Wizard {
        public ulong CharId { get; set; }
        public ulong GameObjectID => CharId;
        public string Name { get; set; } = "Test Wizard";
        public string Zone { get; set; } = "WizardCity/WC_Ravenwood";
        public string PreviousZone { get; set; }
        public Imcodec.Math.Vector3 Location { get; set; }
        public Imcodec.Math.Vector3 Orientation { get; set; }
        public NameBehavior PlayerNameBehavior => new(Name);

        public sealed class NameBehavior(string name) { public string GetWizardName() => name; }
    }
}

namespace Imlight.CoreLib.WizardData.Models.World {
    public class DropTableResult {
        public int GoldAmount { get; set; }
    }
}
