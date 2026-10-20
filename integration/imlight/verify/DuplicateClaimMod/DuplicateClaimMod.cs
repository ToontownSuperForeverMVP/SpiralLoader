using Imcodec.MessageLayer.Generated;
using Imlight.CoreLib.Modding;
using Imlight.CoreLib.Shared.Networking;
using SpiralLoader.Api;

namespace DuplicateClaimVerifyMod;

// Registers a service and a job id of its own, then claims client-process-verify's menu job id. The registry
// rejects the duplicate, Initialize throws, and SpiralLoader rolls back everything this mod registered: its
// service is never created and its own job id goes back to Imlight.
public sealed class DuplicateClaimMod : ISpiralMod {

    public void Initialize(IModContext context) {
        context.Registries.Get(ImlightRegistries.GameMessageServices).Register("rolled_back", typeof(RolledBackService));
        context.Registries.Get(ImlightRegistries.ClientProcessJobs).Register("rolled_back", 770301u);
        context.Registries.Get(ImlightRegistries.ClientProcessJobs).Register("stolen_menu", 770101u);
    }

}

public sealed class RolledBackService : ModMessageService {

    [MessageHandler(typeof(GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS))]
    private void OnReply(GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS message)
        => Console.WriteLine($"VERIFY: duplicate-claim-verify received job={message.JobID} (must not happen)");

}
