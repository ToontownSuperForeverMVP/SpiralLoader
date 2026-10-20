using Imcodec.MessageLayer.Generated;
using Imlight.CoreLib.Modding;
using Imlight.CoreLib.Shared.Networking;
using SpiralLoader.Api;

namespace IsolationVerifyMod;

// A second mod handling the same message types as client-process-verify. It must only ever see replies for its
// own job id, cannot start a process under another mod's id, and its faults must not disturb anyone else.
public sealed class IsolationMod : ISpiralMod {

    public const uint OwnJob = 770201;
    public const uint ForeignJob = 770101;

    public void Initialize(IModContext context) {
        context.Registries.Get(ImlightRegistries.ClientProcessJobs).Register("own", OwnJob);
        context.Registries.Get(ImlightRegistries.LoginMessageServices).Register("login_listener", typeof(IsolationLoginService));
        context.Registries.Get(ImlightRegistries.GameMessageServices).Register("game_listener", typeof(IsolationGameService));
        context.Registries.Get(ImlightRegistries.GameMessageServices).Register("broken", typeof(BrokenService));
    }

}

public sealed class IsolationLoginService : ModMessageService {

    private readonly List<string> _received = [];

    [MessageHandler(typeof(GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS))]
    private void OnReply(GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS message) => _received.Add($"{message.JobID}:{message.Message}");

    protected override void OnSessionEnded()
        => Console.WriteLine($"VERIFY: {OwnerModId} login session ended session={SessionId} received=[{string.Join(",", _received)}]");

}

public sealed class IsolationGameService : ModMessageService {

    private readonly List<string> _received = [];

    [MessageHandler(typeof(GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS))]
    private void OnReply(GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS message) {
        _received.Add($"{message.JobID}:{message.Message}");
        switch (message.Message) {
            case "isolation:ping":
                try {
                    StartClientProcess(ForeignJob(), "Isolation/Steal.lua");
                    Console.WriteLine("VERIFY: isolation-verify started a process under a foreign job id");
                } catch (InvalidOperationException ex) {
                    Console.WriteLine($"VERIFY: isolation-verify refused: {ex.Message}");
                }
                StartClientProcess(IsolationMod.OwnJob, "Isolation/Pong.lua");
                break;
            case "boom":
                throw new InvalidOperationException("isolation-verify handler fault on purpose");
        }
    }

    [MessageHandler(typeof(GAME_5_PROTOCOL.MSG_CLIENT_PROCESS_TERMINATED))]
    private void OnTerminated(GAME_5_PROTOCOL.MSG_CLIENT_PROCESS_TERMINATED message) => _received.Add($"terminated:{message.JobID}");

    protected override void OnSessionEnded()
        => Console.WriteLine($"VERIFY: {OwnerModId} game session ended account={GetSocketAccount().Username} received=[{string.Join(",", _received)}]");

    private static uint ForeignJob() => IsolationMod.ForeignJob;

}

// Fails to construct in every session: Imlight must log it against this mod and carry on without it.
public sealed class BrokenService : ModMessageService {

    public BrokenService() => throw new InvalidOperationException("broken-service constructor fault on purpose");

    [MessageHandler(typeof(GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS))]
    private void OnReply(GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS message)
        => Console.WriteLine("VERIFY: isolation-verify broken service received a message");

}
