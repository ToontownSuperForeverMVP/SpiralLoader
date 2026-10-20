// Functional stubs for the Imlight types the harness cannot compile (they depend on Imcodec generated code, RavenDB
// or real sockets). Signatures follow the pinned Imlight source; behaviour is the minimum Program.Main and the
// scenarios need. The session plumbing under test (SessionActor, MessageService, the dispatcher) is NOT stubbed.
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
    // SessionActor, MessageService, ReceiveProtocolDispatcher and MessageHandlerTable are Imlight's real files.
    // Only the socket actors are stubbed: the listener does nothing (scenarios inject packets with
    // MSG_RECEIVEDPACKET, exactly what it would send) and the sender records what would go to the client.
    internal sealed class SocketListener : ReceiveActor, IDisposable {
        public SocketListener(IActorRef session, System.Net.Sockets.Socket socket, ushort sessionId) => ReceiveAny(_ => { });

        public void Dispose() { }
    }

    internal sealed class SocketSender : ReceiveActor, IDisposable {
        public SocketSender(IActorRef session, System.Net.Sockets.Socket socket, ushort sessionId)
            => ReceiveAny(message => ScenarioWorld.SocketOf(sessionId).Enqueue(message));

        public void Dispose() { }
    }

    public static class Server {
        public static readonly TimeSpan StartListeningTimeout = TimeSpan.FromSeconds(30);
    }
}

namespace Imlight.CoreLib.Shared.Services {
    using Imlight.CoreLib.Shared.Networking;
    using Imlight.CoreLib.Shared.Packets;
    using Imlight.CoreLib.WizardData.Models.Player;

    internal sealed class ControlService(SessionActor s) : MessageService(s), IHandshakeService { }

    // Answers the account ask MessageService.GetSocketAccount makes, with the account the scenario bound to the session.
    internal sealed class AccountService(SessionActor s) : MessageService(s) {
        public Account Account => ScenarioWorld.AccountOf(SessionActor.SessionID);

        [MessageHandler(typeof(ACCOUNT_104_PROTOCOL.MSG_QUERYACCOUNT))]
        private void ReceiveQueryAccount() => Sender.Tell(new ACCOUNT_104_PROTOCOL.MSG_ACCOUNT { Account = Account });
    }
}

namespace Imlight.CoreLib.Game.Services {
    using Imlight.CoreLib.Shared.Networking;
    internal sealed class AttachService(SessionActor s) : MessageService(s) { }
    internal sealed class ClientService(SessionActor s) : MessageService(s) { }
    internal sealed class MoveService(SessionActor s) : MessageService(s) { }
    internal sealed class ZoneService(SessionActor s) : MessageService(s) {
        public IActorRef ZoneActor => null;
    }
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
                HookScenarios.Run(Context.System, login);
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
                // Mod-provided services: the mod service hosts (ModMessageService) and any type from another assembly.
                var extra = services.Where(t => t.Namespace == "Imlight.CoreLib.Modding" || !t.Assembly.Equals(typeof(LoginServer).Assembly))
                    .Select(t => t.FullName).Order(StringComparer.Ordinal);
                Console.WriteLine($"VERIFY: {label} services={services.Count} extra=[{string.Join(",", extra)}]");
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
        public static void RemoveOnlinePlayer(ushort sessionId) { }
        public static Imlight.CoreLib.WizardData.Models.Misc.OnlinePlayer GetOnlinePlayer(ulong characterId) => null;
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
        public Account Account { get; set; }
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

// ZONE_102_PROTOCOL.cs and CHARACTER_103_PROTOCOL.cs need dozens of generated zone and template types; these are the
// members MessageService uses, with the real names. SERVER_100, SERVICE_101, ACCOUNT_104 and PROCESS_107 are real.
namespace Imlight.CoreLib.Shared.Packets {
    using Imlight.CoreLib.Game.Zone.Core;
    using Imlight.CoreLib.Shared.Networking;
    using Imlight.CoreLib.WizardData.Models.Player;

    [Flags]
    public enum ZoneBroadcastTarget : byte {
        None = 0,
        Players = 1 << 0,
        All = 0x3F,
        AllNoPlayers = All & ~Players,
    }

    public class ZONE_102_PROTOCOL {
        public sealed class MSG_ZONEBROADCAST : IServerMessage {
            public byte MessageOrder { get; } = 10;
            public byte ServiceID { get; } = 102;
            public IMessage Message;
            public List<IServerMessage> Messages;
            public bool Selfless;
            public IActorRef Sender;
            public ZoneBroadcastTarget Targets;
        }

        public sealed class MSG_QUERYZONEENTITY : IServerMessage {
            public byte MessageOrder { get; } = 11;
            public byte ServiceID { get; } = 102;
            public ulong GlobalID;
        }

        public sealed class MSG_QUERYZONEENTITYRSP : IServerMessage {
            public byte MessageOrder { get; } = 12;
            public byte ServiceID { get; } = 102;
            public ZoneEntity ZoneObject;
        }

        public sealed class MSG_ZONETRANSFER : IServerMessage {
            public byte MessageOrder { get; } = 13;
            public byte ServiceID { get; } = 102;
            public string DestinationLocation;
            public string DestinationZone;
            public bool SendToClient;
            public bool IsPrivate;
            public ulong OwnerCharId;
        }
    }

    public class CHARACTER_103_PROTOCOL {
        public class MSG_QUERYACTIVEWIZARD : IServerMessage {
            public byte MessageOrder { get; } = 2;
            public byte ServiceID { get; } = 103;
        }

        public class MSG_CHARACTER : IServerMessage {
            public byte MessageOrder { get; } = 3;
            public byte ServiceID { get; } = 103;
            public Wizard Wizard;
            public Imcodec.ObjectProperty.TypeCache.CoreObject WizardGameObject;
        }

        public sealed class MSG_DOTELEPORTEFFECTS : IServerMessage {
            public byte MessageOrder { get; } = 5;
            public byte ServiceID { get; } = 103;
        }
    }
}
