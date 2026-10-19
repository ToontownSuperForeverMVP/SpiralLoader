// Drives the REAL patched Imlight services (CharacterService, GameTransitionService, MinigameService) with a
// recording stub session and a stub game server, and prints "VERIFY:" lines that verify-patch.sh asserts.
// The stubs are in Stubs.cs / ImcodecStubs.cs; nothing here stands in for the code under test.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Imcodec.IO;
using Imcodec.MessageLayer;
using Imcodec.MessageLayer.Generated;
using Imlight.CoreLib.Login.Services;
using Imlight.CoreLib.Game.Services;
using Imlight.CoreLib.Modding;
using Imlight.CoreLib.Shared.Networking;
using Imlight.CoreLib.Shared.Packets;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imlight.CoreLib.Shared.Networking {
    // Maps a stub session to its account (the real MessageService asks an AccountService for it).
    public static class ScenarioWorld {
        private static readonly ConcurrentDictionary<SessionActor, Account> s_accounts = new();

        public static void Bind(SessionActor session, Account account) => s_accounts[session] = account;

        public static Account AccountOf(SessionActor session) => s_accounts[session];
    }
}

namespace Imlight.CoreLib.Login {
    internal static class HookScenarios {

        private const ulong CharacterId = 1;

        public static void Run(ActorSystem system) {
            try {
                Claims();
                CharacterList(system, "menu-user");
                CharacterList(system, "plain-user");
                GameServerSelection(system, "default-user");
                GameServerSelection(system, "route-user");
                GameServerSelection(system, "veto-user");
                GameServerSelection(system, "unknown-realm-user");
                Minigame(system);
            } catch (Exception ex) {
                Console.WriteLine($"VERIFY: scenario FAILED {ex}");
            }
        }

        private static void Claims()
            => Console.WriteLine("VERIFY: scenario claims " + string.Join(" ", new uint[] { 770101, 770102, 424242 }
                .Select(id => $"{id}={ImlightModding.IsClientProcessJobClaimed(id)}")));

        private static void CharacterList(ActorSystem system, string username) {
            var (session, socket, _, account) = NewSession(system, username);
            account.Characters.Add(new Wizard { CharId = CharacterId });

            var service = system.ActorOf(Props.Create(typeof(CharacterService), session));
            service.Tell(new LOGIN_108_PROTOCOL.MSG_REQUESTCHARACTERLIST());
            Settle(socket, TimeSpan.FromMilliseconds(500));

            Console.WriteLine($"VERIFY: scenario character-list {username} socket=[{Describe(socket)}]");
        }

        private static void GameServerSelection(ActorSystem system, string username) {
            var (session, socket, server, account) = NewSession(system, username);
            account.Characters.Add(new Wizard { CharId = CharacterId });

            var service = system.ActorOf(Props.Create(typeof(GameTransitionService), session));
            service.Tell(new LOGIN_7_PROTOCOL.MSG_SELECTCHARACTER { CharID = CharacterId });
            WaitFor(socket, queue => queue.Contains("Close") || queue.OfType<LOGIN_7_PROTOCOL.MSG_CHARACTERSELECTED>().Count() >= 2);

            // Whether SendErrorToSocket also closes the session differs between Imlight revisions, so it is reported but not asserted.
            var closed = socket.ToArray().Contains("Close");
            var sent = new ConcurrentQueue<object>(socket.ToArray().Where(m => m is not string));
            Console.WriteLine($"VERIFY: scenario select {username} server-asks=[{Describe(server)}] socket=[{Describe(sent)}] closed={closed}");
        }

        private static void Minigame(ActorSystem system) {
            var (session, _, _, _) = NewSession(system, "minigame-user");

            var service = system.ActorOf(Props.Create(typeof(MinigameService), session));
            service.Tell(new GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS { JobID = 770101, Message = "claimed" });
            service.Tell(new GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS { JobID = 424242, Message = "unclaimed" });
            SpinWait.SpinUntil(() => Volatile.Read(ref MessageEncoder.DecodeCalls) >= 1, TimeSpan.FromSeconds(5));
            Thread.Sleep(200);

            Console.WriteLine($"VERIFY: scenario minigame decodes={Volatile.Read(ref MessageEncoder.DecodeCalls)}");
        }

        // ------------------------------------------------------------------------------------------------

        private static (SessionActor Session, ConcurrentQueue<object> Socket, ConcurrentQueue<object> Server, Account Account)
            NewSession(ActorSystem system, string username) {
            var socket = new ConcurrentQueue<object>();
            var asks = new ConcurrentQueue<object>();
            var session = new SessionActor {
                ActorRef = system.ActorOf(Props.Create(() => new Recorder(socket))),
                ServerRef = system.ActorOf(Props.Create(() => new StubGameServer(asks))),
            };
            var account = new Account(username, (ulong) username.GetHashCode());
            ScenarioWorld.Bind(session, account);

            return (session, socket, asks, account);
        }

        private static void WaitFor(ConcurrentQueue<object> queue, Func<object[], bool> condition)
            => SpinWait.SpinUntil(() => condition(queue.ToArray()), TimeSpan.FromSeconds(10));

        // Waits until the queue has been quiet for the given time (the CharacterService sends a variable run of messages).
        private static void Settle(ConcurrentQueue<object> queue, TimeSpan quiet) {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            var last = -1;
            while (DateTime.UtcNow < deadline) {
                Thread.Sleep(quiet);
                if (queue.Count == last && last > 0) {
                    return;
                }
                last = queue.Count;
            }
        }

        private static string Describe(ConcurrentQueue<object> queue) => string.Join(",", queue.ToArray().Select(Describe));

        private static string Describe(object message) => message switch {
            string text => text,
            LOGIN_7_PROTOCOL.MSG_STARTCHARACTERLIST => "MSG_STARTCHARACTERLIST",
            LOGIN_7_PROTOCOL.MSG_CHARACTERINFO => "MSG_CHARACTERINFO",
            LOGIN_7_PROTOCOL.MSG_CHARACTERLIST list => $"MSG_CHARACTERLIST(error={list.Error})",
            LOGIN_7_PROTOCOL.MSG_CHARACTERSELECTED s => $"MSG_CHARACTERSELECTED(prep={s.PrepPhase},error={s.Error},port={s.TCPPort})",
            GAME_5_PROTOCOL.MSG_START_CLIENT_PROCESS p => $"MSG_START_CLIENT_PROCESS({p.JobID},{p.ScriptName})",
            SERVER_100_PROTOCOL.MSG_GETBESTSERVER => "MSG_GETBESTSERVER",
            SERVER_100_PROTOCOL.MSG_QUERYREALMSERVER q => $"MSG_QUERYREALMSERVER({q.RealmName})",
            SERVER_100_PROTOCOL.MSG_CREATEKEY => "MSG_CREATEKEY",
            _ => message.GetType().Name,
        };

        private sealed class Recorder : ReceiveActor {
            public Recorder(ConcurrentQueue<object> queue) => ReceiveAny(queue.Enqueue);
        }

        // Answers the asks GameTransitionService makes of the login server / game server.
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
                Receive<SERVER_100_PROTOCOL.MSG_CREATEKEY>(message => {
                    asks.Enqueue(message);
                    Sender.Tell(new SERVER_100_PROTOCOL.MSG_CREATEKEYRSP { Key = ByteString.Of("key") });
                });
            }

            private SERVER_100_PROTOCOL.MSG_SERVERINFO Info(string realm, int port)
                => new() { IP = ByteString.Of("127.0.0.1"), Port = port, RealmName = realm, ActorRef = Self };
        }
    }
}
