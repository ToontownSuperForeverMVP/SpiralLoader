// Functional stubs for the Imlight types the harness cannot compile (they depend on Imcodec generated code).
// Signatures follow the pinned Imlight source; behaviour is the minimum Program.Main needs to run through.
// Lines starting with "VERIFY:" are asserted by verify-patch.sh.
using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Imlight.CoreLib.Game;
using Imlight.CoreLib.Login;
using Imlight.CoreLib.Shared.Packets;

namespace Imcodec.ObjectProperty.TypeCache {
    public class CoreTemplate { }

    // Simulates a generated type named 'Type' (Imlight aliases System.Type for this reason).
    public class Type { }
}

namespace Imlight.CoreLib.Shared.Networking {
    public sealed class SessionActor : ReceiveActor { }

    // Internal in Imlight. Public here only so the verification mod can derive from it without a publicizer.
    public abstract class MessageService(SessionActor sessionActor) : ReceiveActor {
        protected SessionActor SessionActor { get; } = sessionActor;
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
    internal sealed class MinigameService(SessionActor s) : MessageService(s) { }
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
    internal sealed class CharacterService(SessionActor s) : MessageService(s) { }
    internal sealed class GameTransitionService(SessionActor s) : MessageService(s) { }
    internal sealed class LoginAFKService(SessionActor s) : MessageService(s) { }
}

namespace Imlight.CoreLib.Shared.Packets {
    public static class SERVICE_101_PROTOCOL {
        public sealed class MSG_QUERYUNLOADEDSERVICES { }
        public sealed class MSG_SERVICESLIST { public List<System.Type> Services; }
    }

    public static class SERVER_100_PROTOCOL {
        public sealed class MSG_CREATEGAMESERVER { public string Name; public ushort Port; public string RealmName; }
        public sealed class MSG_INITIALIZE { }
        public sealed class MSG_INITIALIZE_COMPLETE { }
        public sealed class MSG_STARTLISTENING { }
        public sealed class MSG_STARTLISTENING_COMPLETE { }
    }
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
                Receive<SERVER_100_PROTOCOL.MSG_CREATEGAMESERVER>(message => {
                    var game = Context.ActorOf(GameServiceFactory.Props(), $"gameServiceFactory-{message.Port}");
                    Report($"game {message.RealmName}", game);
                });
                Receive<SERVER_100_PROTOCOL.MSG_STARTLISTENING>(_ => Sender.Tell(new SERVER_100_PROTOCOL.MSG_STARTLISTENING_COMPLETE()));
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
        public string Username { get; private set; }
        public AuthLevel AuthLevel { get; set; }
    }

    public class Wizard { }
}

namespace Imlight.CoreLib.WizardData.Models.World {
    public class DropTableResult {
        public int GoldAmount { get; set; }
    }
}
