using SpiralLoader.Api.Commands;
using SpiralLoader.Api.Events;
using SpiralLoader.Api.Logging;
using SpiralLoader.Runtime;

namespace SpiralLoader.Host;

/// <summary>
/// <c>SpiralLoader.Host</c>: loads mods from a directory, runs the server lifecycle events, optionally runs a
/// mod command, then shuts down. Exit codes: 0 success, 1 mod load failure, 2 usage error.
/// </summary>
internal static class Program {

    private const string HostName = "SpiralLoader.Host";

    private static int Main(string[] args) {
        HostArguments arguments;
        try {
            arguments = HostArguments.Parse(args);
        } catch (ArgumentException ex) {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(HostArguments.Usage);
            return 2;
        }

        if (arguments.ShowHelp) {
            Console.WriteLine(HostArguments.Usage);
            return 0;
        }

        var options = arguments.ToOptions(HostName);
        var host = new ModHost(options);
        host.AddRegistry(CommonRegistries.Commands, ModCommandRouter.Validate);

        try {
            host.Load();
        } catch (ModLoadException) {
            // The loader already logged the full report.
            return 1;
        }

        host.Events.Publish(new ServerStartingEvent(HostName));
        host.Events.Publish(new ServerResourcesLoadedEvent(HostName));
        host.Events.Publish(new ServerStartedEvent(HostName));

        var exitCode = 0;
        var router = new ModCommandRouter(host.Registries.Get(CommonRegistries.Commands).Entries, host.Logger);
        foreach (var command in arguments.Commands) {
            if (router.TryExecute(command, caller: "console", callerIsOperator: true, out var reply)) {
                Console.WriteLine($"> {command}{Environment.NewLine}{reply}");
            } else {
                host.Logger.Error($"No mod registered a command for '{command}'.");
                exitCode = 1;
            }
        }

        if (arguments.PlanAssets) {
            var plan = host.PlanAssets();
            foreach (var entry in plan.Overrides) {
                Console.WriteLine($"asset {entry.Kind} {entry.Target}:{entry.EntryPath} <- {entry.ModId}");
            }
            foreach (var conflict in plan.Conflicts) {
                host.Logger.Error(conflict.ToString());
                exitCode = 1;
            }
        }

        host.Events.Publish(new ServerStoppingEvent(HostName));
        host.Shutdown();
        return exitCode;
    }

}

internal sealed class HostArguments {

    public const string Usage = """
        Usage: SpiralLoader.Host [options]
          --mods <dir>            Mods directory (repeatable; default: ./mods)
          --data <dir>            Mod data root (default: ./moddata)
          --config <dir>          Mod config root (default: ./config/mods)
          --profile <name>        Profile name (default: default)
          --disable <id>          Do not load this mod (repeatable)
          --imlight-commit <sha>  Imlight commit to report for compatibility checks
          --client-revision <r>   Client revision to report for compatibility checks
          --compat <strict|warn>  Compatibility pin enforcement (default: strict)
          --continue-on-failure   Keep running when a mod fails (default: abort)
          --run "<command>"       Run a mod command after startup (repeatable)
          --plan-assets           Print the merged asset overlay plan
          --verbose               Debug logging
          --help                  Show this help
        """;

    public List<string> ModDirectories { get; } = [];

    public List<string> Disabled { get; } = [];

    public List<string> Commands { get; } = [];

    public string? DataRoot { get; private set; }

    public string? ConfigRoot { get; private set; }

    public string Profile { get; private set; } = "default";

    public string? ImlightCommit { get; private set; }

    public string? ClientRevision { get; private set; }

    public bool WarnOnlyCompatibility { get; private set; }

    public bool ContinueOnFailure { get; private set; }

    public bool PlanAssets { get; private set; }

    public bool Verbose { get; private set; }

    public bool ShowHelp { get; private set; }

    public static HostArguments Parse(IReadOnlyList<string> args) {
        var result = new HostArguments();
        for (var i = 0; i < args.Count; i++) {
            var arg = args[i];
            string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{arg} needs a value.");
            switch (arg) {
                case "--mods": result.ModDirectories.Add(Value()); break;
                case "--data": result.DataRoot = Value(); break;
                case "--config": result.ConfigRoot = Value(); break;
                case "--profile": result.Profile = Value(); break;
                case "--disable": result.Disabled.Add(Value()); break;
                case "--imlight-commit": result.ImlightCommit = Value(); break;
                case "--client-revision": result.ClientRevision = Value(); break;
                case "--compat":
                    result.WarnOnlyCompatibility = Value() switch {
                        "strict" => false,
                        "warn" => true,
                        var other => throw new ArgumentException($"Unknown compatibility mode '{other}'."),
                    };
                    break;
                case "--continue-on-failure": result.ContinueOnFailure = true; break;
                case "--run": result.Commands.Add(Value()); break;
                case "--plan-assets": result.PlanAssets = true; break;
                case "--verbose": result.Verbose = true; break;
                case "--help" or "-h": result.ShowHelp = true; break;
                default: throw new ArgumentException($"Unknown argument '{arg}'.");
            }
        }
        return result;
    }

    public ModHostOptions ToOptions(string hostName) {
        var currentDirectory = Directory.GetCurrentDirectory();
        var options = new ModHostOptions {
            HostName = hostName,
            Profile = Profile,
            DataRoot = Path.GetFullPath(DataRoot ?? Path.Combine(currentDirectory, "moddata")),
            ConfigRoot = Path.GetFullPath(ConfigRoot ?? Path.Combine(currentDirectory, "config", "mods")),
            ImlightCommit = ImlightCommit,
            ClientRevision = ClientRevision,
            CompatibilityMode = WarnOnlyCompatibility ? CompatibilityMode.WarnOnly : CompatibilityMode.Strict,
            FailurePolicy = ContinueOnFailure ? ModFailurePolicy.ContinueWithoutFailedMods : ModFailurePolicy.AbortStartup,
            MinimumLogLevel = Verbose ? ModLogLevel.Debug : ModLogLevel.Information,
        };
        options.ModDirectories.AddRange(ModDirectories.Count > 0 ? ModDirectories : [Path.Combine(currentDirectory, "mods")]);
        foreach (var id in Disabled) {
            options.DisabledMods.Add(id);
        }
        return options;
    }

}
