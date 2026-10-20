// Stubs for the Imcodec types (wire messages, serializer, value types) that Imlight's services use and that the
// harness cannot build: Imcodec generates them from client-derived inputs that are not distributed.
// Member names and shapes follow how the pinned Imlight source uses them; behaviour is the minimum the scenarios need.
// ASSUMPTION, not verifiable here: MSG_MESSAGE_PROCESS.JobID is a uint (Imlight compares it with uint ids and
// assigns uint process ids to MSG_START_CLIENT_PROCESS.JobID, and Spiral Shuffle compares it with a uint constant).
using System;
using System.Collections.Generic;
using Imcodec.IO;
using Imcodec.Math;
using Imcodec.MessageLayer;
using Imcodec.ObjectProperty;
using Imcodec.ObjectProperty.TypeCache;
using Imlight.CoreLib.WizardData.Models.Player;

namespace Imcodec.IO {
    public sealed class ByteString {
        public static ByteString Of(string text) => new() { Text = text };
        public string Text { get; init; }
    }

    public sealed class WideByteString { }

    public sealed class BitWriter {
        private readonly List<byte> _data = [];
        public void WriteUInt16(ushort value) => _data.AddRange(BitConverter.GetBytes(value));
        public void WriteUInt8(byte value) => _data.Add(value);
        public void WriteBytes(byte[] bytes) => _data.AddRange(bytes);
        public byte[] GetData() => [.. _data];
    }
}

namespace Imcodec.Math {
    public readonly record struct Vector3(float X, float Y, float Z) {
        public static Vector3 Zero => default;
    }

    public readonly record struct Vector4(float X, float Y, float Z, float W);
}

namespace Imcodec.Types {
    public readonly record struct GID(ulong Value);
}

namespace Imcodec.MessageLayer {
    // Same members as Imcodec.MessageLayer.IMessage (minus Encode/Decode, which need BitReader/BitWriter plumbing).
    public interface IMessage {
        byte MessageOrder { get; }
        byte ServiceId { get; }
        byte AccessLevel { get; }
    }

    public abstract class WireMessage : IMessage {
        public byte MessageOrder => 0;
        public byte ServiceId => 0;
        public byte AccessLevel => 0;
    }

    // Records the payload of every client process message MinigameService decodes (it prepends an 8-byte header),
    // then reports "nothing decoded", which MinigameService logs and drops.
    public static class MessageEncoder {
        public static readonly System.Collections.Concurrent.ConcurrentQueue<string> Decoded = new();

        public static List<IMessage> Decode(byte[] data) {
            Decoded.Enqueue(System.Text.Encoding.UTF8.GetString(data, 8, data.Length - 8));
            return [];
        }
    }
}

namespace Imcodec.MessageLayer.Generated {
    using Imcodec.IO;
    using Imcodec.Math;
    using Imcodec.Types;

    public static class GAME_5_PROTOCOL {
        public sealed class MSG_MESSAGE_PROCESS : WireMessage { public uint JobID; public string Message; }
        public sealed class MSG_CLIENT_PROCESS_TERMINATED : WireMessage { public uint JobID; }
        public sealed class MSG_START_CLIENT_PROCESS : WireMessage { public ulong OwnerGID; public uint JobID; public string ScriptName; }
        public sealed class MSG_KILL_CLIENT_PROCESS : WireMessage { public uint JobID; }
    }

    public static class EXTENDEDBASE_2_PROTOCOL {
        public sealed class MSG_SERVERMESSAGE : WireMessage { public string Message; public byte Modal; }
    }

    public static class WIZARD_12_PROTOCOL {
        public sealed class MSG_MINIGAMESELECT : WireMessage { public byte Index; }
        public sealed class MSG_ENTERMINIGAME : WireMessage { }
        public sealed class MSG_LEAVEMINIGAME : WireMessage { }
    }

    public static class LOGIN_108_PROTOCOL {
        public sealed class MSG_REQUESTCHARACTERLIST : WireMessage { }
    }

    public static class LOGIN_7_PROTOCOL {
        public sealed class MSG_CREATECHARACTER : WireMessage { public ByteString CreationInfo; }
        public sealed class MSG_CREATECHARACTERRESPONSE : WireMessage { public int ErrorCode; }
        public sealed class MSG_DELETECHARACTER : WireMessage { public ulong CharID; }
        public sealed class MSG_DELETECHARACTERRESPONSE : WireMessage { public int ErrorCode; }
        public sealed class MSG_STARTCHARACTERLIST : WireMessage { public string LoginServer; public int PurchasedCharacterSlots; }
        public sealed class MSG_CHARACTERINFO : WireMessage { public ByteString CharacterInfo; }
        public sealed class MSG_CHARACTERLIST : WireMessage { public int Error; }
        public sealed class MSG_LOGINLOGCHARACTERCREATION : WireMessage { public uint Parameter; public uint Stage; }
        public sealed class MSG_REQUESTSERVERLIST : WireMessage { }
        public sealed class MSG_SERVERLIST : WireMessage { }
        public sealed class MSG_SELECTCHARACTER : WireMessage { public ulong CharID; }

        public sealed class MSG_CHARACTERSELECTED : WireMessage {
            public int PrepPhase;
            public int Error;
            public ByteString IP;
            public int TCPPort;
            public int UDPPort;
            public ByteString Key;
            public int Slot;
            public string LoginServer;
            public ulong UserID;
            public ulong CharID;
            public GID ZoneID;
            public string ZoneName;
            public string Location;
        }
    }

    public enum MagicSchool { Fire, Ice, Storm, Myth, Life, Death, Balance }

    public sealed class WizardCharacterCreationInfo {
        public int m_schoolOfFocus;
        public AvatarBehavior m_avatarBehavior = new();
        public sealed class AvatarBehavior { public int m_eGender; }
    }
}

namespace Imcodec.ObjectProperty {
    [Flags] public enum SerializerFlags { None = 0 }

    // Serialize always succeeds with a recognisable payload; Deserialize is only reached by character creation,
    // which the scenarios do not drive.
    public sealed class ObjectSerializer(SerializerFlags Behaviors, bool Versionable) {
        public bool Serialize(object value, PropertyFlags flags, out Imcodec.IO.ByteString data) {
            data = Imcodec.IO.ByteString.Of("serialized");
            return true;
        }

        public bool Deserialize<T>(Imcodec.IO.ByteString data, PropertyFlags flags, out T value) where T : class, new() {
            value = new T();
            return true;
        }
    }
}

namespace Imcodec.ObjectProperty.TypeCache {
    [Flags] public enum PropertyFlags { None = 0, Prop_Transmit = 1, Prop_AuthorityTransmit = 2 }

    public class CoreObject { }
}

namespace Imlight.CoreLib.Shared.Behaviors {
    internal static class BehaviorsNamespaceStub { }
}

namespace Imlight.CoreLib.WizardData.Implementations {
    internal static class ImplementationsNamespaceStub { }
}

namespace Imlight.CoreLib.Shared.Character {
    using Imcodec.MessageLayer.Generated;

    internal static class CharacterHelper {
        public static Wizard CreateCharacterFromCreationInfo(WizardCharacterCreationInfo info) => new();
        public static object GetLoginScreenInfo(Wizard character) => character;
    }
}

namespace Imlight.CoreLib.WizardData.Collections {
    internal static class WizardCollection {
        public static bool DeleteCharacter(ulong characterId) => true;
    }

    internal static class AccountCollection {
        public static bool DeleteCharacterFromAccount(ulong accountId, ulong characterId) => true;
    }
}

namespace Imlight.CoreLib.Shared.Resources {
    internal static class Util {
        public static string GetCompactStringFromVector(Imcodec.Math.Vector3 vector, Imcodec.Math.Vector3 orientation)
            => $"{vector.X},{vector.Y},{vector.Z},{orientation.Z}";
    }
}

namespace Imlight.CoreLib.Game.Minigames {
    internal static class MinigameConfig {
        public static bool IsMinigameZone(string zone) => false;
        public static string GetMinigameScript(string zone) => null;
        public static byte GetMinigameIndex(string zone) => 0;
        public static MinigameInfo GetMinigameInfo(byte index) => null;
    }

    internal sealed class MinigameInfo { public string m_name; public string m_zone; }
}

namespace Imlight.CoreLib.Game.Processes {
    internal sealed class MinigameProcess(string name, uint processId, byte minigameIndex) : Akka.Actor.ReceiveActor { }
}
