using Imcodec.MessageLayer.Generated;
using Imlight.CoreLib.Modding;
using Imlight.CoreLib.Shared.Networking;
using SpiralLoader.Api;

namespace ClientProcessVerifyMod;

// The shape of Spiral Shuffle's menu and enemy inspector: claim two job ids, start the menu script at character
// select, and handle the client's MSG_MESSAGE_PROCESS replies in the mod's own message services.
public sealed class ClientProcessMod : ISpiralMod {

    public const uint MenuJob = 770101;
    public const uint InspectorJob = 770102;

    public void Initialize(IModContext context) {
        var jobs = context.Registries.Get(ImlightRegistries.ClientProcessJobs);
        jobs.Register("menu", MenuJob);
        jobs.Register("inspector", InspectorJob);

        context.Registries.Get(ImlightRegistries.LoginMessageServices).Register("menu_replies", typeof(MenuReplyService));
        context.Registries.Get(ImlightRegistries.GameMessageServices).Register("inspector", typeof(InspectorService));

        context.Events.Subscribe<CharacterListSentEvent>(e => {
            if (e.Account.Username == "menu-user") {
                e.Send(new GAME_5_PROTOCOL.MSG_START_CLIENT_PROCESS { JobID = MenuJob, ScriptName = "Verify/Menu.lua" });
            }
        });
    }

}

// Records what one session's instance received and prints it when the session ends, so the harness output shows
// exactly which replies reached which mod.
public abstract class RecordingService : ModMessageService {

    private readonly List<string> _received = [];
    private string _account = "-";

    protected abstract string Side { get; }

    protected void Record(string what) {
        _account = GetSocketAccount().Username;
        _received.Add(what);
    }

    protected string AccountName => _account;

    protected override void OnSessionEnded()
        => Console.WriteLine($"VERIFY: {OwnerModId} {Side} session ended account={_account} received=[{string.Join(",", _received)}]");

}

// Login session: the menu's reply. Answers by closing the menu process on the client.
public sealed class MenuReplyService : RecordingService {

    protected override string Side => "login";

    [MessageHandler(typeof(GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS))]
    private void OnMenuReply(GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS message) {
        Record($"{message.JobID}:{message.Message}");
        Console.WriteLine($"VERIFY: client-process-verify login reply job={message.JobID} account={AccountName} message={message.Message}");
        SendToSocket(new GAME_5_PROTOCOL.MSG_KILL_CLIENT_PROCESS { JobID = message.JobID });
    }

}

// Game session: the inspector's replies. Answers a query by starting a result script under its own job id.
public sealed class InspectorService : RecordingService {

    protected override string Side => "game";

    [MessageHandler(typeof(GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS))]
    private void OnInspectorReply(GAME_5_PROTOCOL.MSG_MESSAGE_PROCESS message) {
        Record($"{message.JobID}:{message.Message}");
        if (message.Message == "inspect:42") {
            StartClientProcess(ClientProcessMod.InspectorJob, "Verify/InspectorResult.lua");
        }
    }

    [MessageHandler(typeof(GAME_5_PROTOCOL.MSG_CLIENT_PROCESS_TERMINATED))]
    private void OnTerminated(GAME_5_PROTOCOL.MSG_CLIENT_PROCESS_TERMINATED message) => Record($"terminated:{message.JobID}");

}
