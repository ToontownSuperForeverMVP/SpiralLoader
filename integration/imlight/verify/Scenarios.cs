// Drives REAL patched Imlight sessions: each scenario creates Imlight's own SessionActor (with its real
// MessageService children from the real, patched Login/GameServiceFactory), injects synthetic client packets the
// way SocketListener does (MSG_RECEIVEDPACKET), and prints "VERIFY:" lines that the verify scripts assert.
// Stubs (Stubs.cs / ImcodecStubs.cs) stand in for sockets, the game server, Imcodec-generated messages and the
// built-in services the scenarios do not exercise; nothing here stands in for the code under test.
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Akka.Actor;
using Imcodec.IO;
using Imcodec.MessageLayer;
using Imcodec.MessageLayer.Generated;
using Imlight.CoreLib.Game;
using Imlight.CoreLib.Modding;
using Imlight.CoreLib.Shared.Networking;
using Imlight.CoreLib.Shared.Packets;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Shared.Networking {
    // What the stub socket actors and stub account service need to know about a scenario session.
    public static class ScenarioWorld {
        private static readonly ConcurrentDictionary<ushort, Account> s_accounts = new();
        private static readonly ConcurrentDictionary<ushort, ConcurrentQueue<object>> s_sockets = new();

        public static void Bind(ushort sessionId, Account account) => s_accounts[sessionId] = account;

        public static Account AccountOf(ushort sessionId) => s_accounts[sessionId];

        public static ConcurrentQueue<object> SocketOf(ushort sessionId) => s_sockets.GetOrAdd(sessionId, _ => new());
    }
}

namespace Imlight.CoreLib.Login {
    internal static class HookScenarios {

        private const ulong CharacterId = 1;

        // Job ids the test mods claim (or, for 770301 and 424242, do not end up claiming).
        private const uint MenuJob = 770101;           // client-process-verify, started from CharacterListSentEvent
        private const uint InspectorJob = 770102;      // client-process-verify, game session
        private const uint IsolationJob = 770201;      // isolation-verify
        private const uint RolledBackJob = 770301;     // duplicate-claim-verify claims it, then fails to initialize
        private const uint UnclaimedJob = 424242;      // nobody: Imlight's MinigameService keeps it

        private static int s_nextSessionId = 100;

        private sealed record Session(IActorRef Actor, ushort Id, ConcurrentQueue<object> Socket,
                                      ConcurrentQueue<object> ServerAsks, Account Account);

        public static void Run(ActorSystem system, IActorRef loginFactory) {
            try {
                var gameFactory = system.ActorOf(GameServiceFactory.Props(), "scenarioGameServiceFactory");

                // The bridge exists because MessageService is internal: a mod can only see ModMessageService.
                Console.WriteLine($"VERIFY: scenario visibility MessageService.public={typeof(MessageService).IsPublic} " +
                                  $"ModMessageService.public={typeof(ModMessageService).IsPublic}");
                Claims();
                CharacterListAndMenuReply(system, loginFactory, "menu-user");
                CharacterListAndMenuReply(system, loginFactory, "plain-user");
                GameServerSelection(system, loginFactory, "default-user");
                GameServerSelection(system, loginFactory, "route-user");
                GameServerSelection(system, loginFactory, "veto-user");
                GameServerSelection(system, loginFactory, "unknown-realm-user");
                GameClientProcessReplies(system, gameFactory);
                Console.WriteLine("VERIFY: scenarios done");
            } catch (Exception ex) {
                Console.WriteLine($"VERIFY: scenario FAILED {ex}");
            }
        }

        private static void Claims()
            => Console.WriteLine("VERIFY: scenario claims " + string.Join(" ",
                new[] { MenuJob, InspectorJob, IsolationJob, RolledBackJob, UnclaimedJob }
                    .Select(id => $"{id}={ImlightModding.IsClientProcessJobClaimed(id)}")));

        // Login session: the character list (CharacterListSentEvent may start the menu process), then the client's
        // synthetic reply for the menu job, then the session closes.
        private static void CharacterListAndMenuReply(ActorSystem system, IActorRef factory, string username) {
            var session = Open(system, factory, username);
            session.Account.Characters.Add(new Wizard { CharId = CharacterId });

            Receive(session, new LOGIN_108_PROTOCOL.MSG_REQUESTCHARACTERLIST());
            Settle(() => session.Socket.Count);
            var listed = session.Socket.Count;
            Console.WriteLine($"VERIFY: scenario character-list {username} socket=[{Describe(session.Socket)}]");

            Receive(session, new GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS { JobID = MenuJob, Message = "menu:shuffle" });
            Settle(() => session.Socket.Count);
            Console.WriteLine($"VERIFY: scenario menu-reply {username} socket-after=[{Describe(session.Socket.Skip(listed))}]");

            Console.WriteLine($"VERIFY: scenario close {username} closed={Close(session)}");
        }

        private static void GameServerSelection(ActorSystem system, IActorRef factory, string username) {
            var session = Open(system, factory, username);
            session.Account.Characters.Add(new Wizard { CharId = CharacterId });

            Receive(session, new LOGIN_7_PROTOCOL.MSG_SELECTCHARACTER { CharID = CharacterId });
            WaitFor(() => session.Socket.OfType<LOGIN_7_PROTOCOL.MSG_CHARACTERSELECTED>().Count() >= 2);

            // At b0798716 GameTransitionService closes the session after an error; at 2a16010e it does not. The
            // revision-specific expected-output files assert which.
            var closed = WaitFor(() => IsDeallocated(session), TimeSpan.FromMilliseconds(750));
            Console.WriteLine($"VERIFY: scenario select {username} server-asks=[{Describe(session.ServerAsks.Where(m => m is not SERVER_100_PROTOCOL.MSG_DEALLOCATESOCKET))}] " +
                              $"socket=[{Describe(session.Socket)}] closed-by-imlight={closed}");
            Close(session);
        }

        // Game session: replies for every kind of job id, in one session, through the real dispatcher. The mods
        // print what each of their services received when the session ends; MinigameService's decodes are listed here.
        private static void GameClientProcessReplies(ActorSystem system, IActorRef factory) {
            var session = Open(system, factory, "game-user");
            var decodedBefore = MessageEncoder.Decoded.Count;

            Receive(session, new GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS { JobID = InspectorJob, Message = "inspect:42" });
            Receive(session, new GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS { JobID = IsolationJob, Message = "isolation:ping" });
            Receive(session, new GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS { JobID = RolledBackJob, Message = "rolled-back" });
            Receive(session, new GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS { JobID = UnclaimedJob, Message = "unclaimed" });
            Receive(session, new GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS { JobID = IsolationJob, Message = "boom" });
            Receive(session, new GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS { JobID = InspectorJob, Message = "after-fault" });
            Receive(session, new GAME_5_PROTOCOL.MSG_CLIENT_PROCESS_TERMINATED { JobID = InspectorJob });
            Settle(() => session.Socket.Count + MessageEncoder.Decoded.Count);

            Console.WriteLine($"VERIFY: scenario game-replies socket=[{Describe(session.Socket)}]");
            Console.WriteLine($"VERIFY: scenario game-replies minigame-decoded=[{string.Join(",", MessageEncoder.Decoded.Skip(decodedBefore))}]");
            Console.WriteLine($"VERIFY: scenario close game-user closed={Close(session)}");
        }

        // ------------------------------------------------------------------------------------------------

        private static Session Open(ActorSystem system, IActorRef factory, string username) {
            var id = (ushort) Interlocked.Increment(ref s_nextSessionId);
            var account = new Account(username, id);
            ScenarioWorld.Bind(id, account);
            var asks = new ConcurrentQueue<object>();
            var server = system.ActorOf(Props.Create(() => new StubGameServer(asks)), $"scenarioServer-{id}");

            // SessionActor only reads the socket's remote endpoint; a connected UDP socket sends nothing.
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(new IPEndPoint(IPAddress.Loopback, 9));

            var actor = system.ActorOf(SessionActor.Props(socket, id, server, factory), $"scenarioSession-{id}");
            actor.Tell(new SERVICE_101_PROTOCOL.MSG_GETALLSERVICES());

            return new Session(actor, id, ScenarioWorld.SocketOf(id), asks, account);
        }

        // Exactly what SocketListener does with a decoded client packet.
        private static void Receive(Session session, IMessage packet)
            => session.Actor.Tell(new SERVER_100_PROTOCOL.MSG_RECEIVEDPACKET { Packet = packet });

        // Closes the session the way a disconnect does and waits for the session to free its id.
        private static bool Close(Session session) {
            session.Actor.Tell("Close");
            var closed = WaitFor(() => IsDeallocated(session));
            Thread.Sleep(300);

            return closed;
        }

        private static bool IsDeallocated(Session session)
            => session.ServerAsks.OfType<SERVER_100_PROTOCOL.MSG_DEALLOCATESOCKET>().Any();

        private static bool WaitFor(Func<bool> condition, TimeSpan? timeout = null)
            => SpinWait.SpinUntil(condition, timeout ?? TimeSpan.FromSeconds(10));

        // Waits until a counter has been stable for a while (services answer with variable runs of messages).
        private static void Settle(Func<int> counter) {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            var last = -1;
            while (DateTime.UtcNow < deadline) {
                Thread.Sleep(400);
                var now = counter();
                if (now == last) {
                    return;
                }
                last = now;
            }
        }

        private static string Describe(System.Collections.Generic.IEnumerable<object> messages)
            => string.Join(",", messages.Select(Describe));

        private static string Describe(object message) => message switch {
            string text => text,
            LOGIN_7_PROTOCOL.MSG_STARTCHARACTERLIST => "MSG_STARTCHARACTERLIST",
            LOGIN_7_PROTOCOL.MSG_CHARACTERINFO => "MSG_CHARACTERINFO",
            LOGIN_7_PROTOCOL.MSG_CHARACTERLIST list => $"MSG_CHARACTERLIST(error={list.Error})",
            LOGIN_7_PROTOCOL.MSG_CHARACTERSELECTED s => $"MSG_CHARACTERSELECTED(prep={s.PrepPhase},error={s.Error},port={s.TCPPort})",
            GAME_5_PROTOCOL.MSG_START_CLIENT_PROCESS p => $"MSG_START_CLIENT_PROCESS({p.JobID},{p.ScriptName})",
            GAME_5_PROTOCOL.MSG_KILL_CLIENT_PROCESS k => $"MSG_KILL_CLIENT_PROCESS({k.JobID})",
            SERVER_100_PROTOCOL.MSG_GETBESTSERVER => "MSG_GETBESTSERVER",
            SERVER_100_PROTOCOL.MSG_QUERYREALMSERVER q => $"MSG_QUERYREALMSERVER({q.RealmName})",
            SERVER_100_PROTOCOL.MSG_PLAYERENQUEUED => "MSG_PLAYERENQUEUED",
            SERVER_100_PROTOCOL.MSG_CREATEKEY => "MSG_CREATEKEY",
            _ => message.GetType().Name,
        };

        // Plays the login server (best server, realm lookup, session deallocation) and the game server (enqueue,
        // session key) for one session.
        private sealed class StubGameServer : ReceiveActor {
            private static readonly string[] s_realms = ["Alpha", "Shuffle"];

            public StubGameServer(ConcurrentQueue<object> asks) {
                Receive<SERVER_100_PROTOCOL.MSG_GETBESTSERVER>(message => {
                    asks.Enqueue(message);
                    Sender.Tell(Info("Default", 12000));
                });
                Receive<SERVER_100_PROTOCOL.MSG_QUERYREALMSERVER>(message => {
                    asks.Enqueue(message);
                    Sender.Tell(Array.IndexOf(s_realms, message.RealmName) >= 0 ? Info(message.RealmName, 13000) : Info(null, 0));
                });
                Receive<SERVER_100_PROTOCOL.MSG_PLAYERENQUEUED>(message => {
                    asks.Enqueue(message);
                    Sender.Tell(new LOGIN_7_PROTOCOL.MSG_CHARACTERSELECTED { PrepPhase = 0 });
                });
                Receive<SERVER_100_PROTOCOL.MSG_CREATEKEY>(message => {
                    asks.Enqueue(message);
                    Sender.Tell(new SERVER_100_PROTOCOL.MSG_CREATEKEYRSP { Key = ByteString.Of("key") });
                });
                Receive<SERVER_100_PROTOCOL.MSG_DEALLOCATESOCKET>(asks.Enqueue);
            }

            private SERVER_100_PROTOCOL.MSG_SERVERINFO Info(string realm, int port)
                => new() { IP = ByteString.Of("127.0.0.1"), Port = port, RealmName = realm, ActorRef = Self };
        }
    }
}
