using SpiralLoader.Api;
using SpiralLoader.Api.Commands;
using SpiralLoader.Api.Events;
using SpiralLoader.Api.Logging;
using SpiralLoader.Api.Manifest;
using SpiralLoader.Api.Registries;
using SpiralLoader.Api.Versioning;

namespace ApiTour;

/// <summary>An event type defined by the mod, deriving from the API's cancellable base class.</summary>
public sealed class TourEvent : CancellableEvent {
    public int Value { get; set; }
}

/// <summary>Touches the public API broadly. Run the "tour" command to see which checks still pass.</summary>
public sealed class TourMod : ISpiralMod {

    private static readonly RegistryKey<string> s_notes = new("tour:notes");
    private readonly List<string> _log = [];
    private readonly List<string> _failures = [];

    public void Initialize(IModContext context) {
        // Manifest and identity.
        Check("manifest", context.Manifest.Id == "api-tour" && context.Manifest.Version == SemanticVersion.Parse("1.0.0")
            && context.Manifest.SchemaVersion == SpiralLoaderInfo.ManifestSchemaVersion
            && context.Manifest.GetEntrypoints(SpiralLoaderInfo.ServerEntrypoint).Count == 2
            && context.Manifest.Depends.Any(d => d.ModId == SpiralLoaderInfo.LoaderModId && d.Range.IsSatisfiedBy(SpiralLoaderInfo.ApiVersion))
            && context.Manifest.Recommends.Single().ModId == "not-installed"
            && context.Manifest.Assets.Count == 0 && context.Manifest.Compatibility.ImlightCommits.Count == 0
            && context.Manifest.Name == "API Tour" && context.Manifest.Authors.Count == 1);

        // Environment and the read-only mod query.
        var environment = context.Environment;
        Check("environment", environment.EntrypointKind == SpiralLoaderInfo.ServerEntrypoint && environment.Profile.Length > 0
            && environment.HostName.Length > 0 && Directory.Exists(environment.ModDirectory)
            && environment.DataDirectory.Contains("api-tour") && environment.ConfigDirectory.Contains("api-tour"));
        Check("mods", context.Mods.IsLoaded(SpiralLoaderInfo.LoaderModId) && !context.Mods.IsLoaded("not-installed")
            && context.Mods.GetManifest("not-installed") is null);

        // Logging.
        context.Logger.Info("tour: logger works");
        context.Logger.Debug("tour: debug");
        context.Logger.Warn("tour: warn", new InvalidOperationException("sample"));
        Check("logger", context.Logger.Source == "api-tour" && context.Logger.IsEnabled(ModLogLevel.Error));

        // Events: lifecycle subscriptions now; the mod-defined event is published later, from the command, because the
        // bus is frozen once every mod has initialized (publishing stays allowed, subscribing does not).
        context.Events.Subscribe<TourEvent>(e => { _order.Add("low"); }, EventPriority.Low);
        context.Events.Subscribe<TourEvent>(e => { _order.Add("high"); e.Value += 1; }, EventPriority.High);
        context.Events.Subscribe<TourEvent>(e => { _order.Add("normal"); e.Value += 10; if (e.Value > 50) { e.Cancel(); } });
        context.Events.Subscribe<TourEvent>(e => _order.Add("monitor"), EventPriority.Monitor);
        Check("events.subscribed", context.Events.HasSubscribers<TourEvent>() && !context.Events.HasSubscribers<ModCommandInvocation>());
        context.Events.Subscribe<ModsLoadedEvent>(e => _log.Add("loaded:" + string.Join(",", e.LoadOrder)));
        context.Events.Subscribe<ServerStartingEvent>(e => _log.Add("starting:" + e.HostName));
        context.Events.Subscribe<ServerResourcesLoadedEvent>(e => _log.Add("resources:" + e.HostName));
        context.Events.Subscribe<ServerStartedEvent>(e => _log.Add("started:" + e.HostName));
        context.Events.Subscribe<ServerStoppingEvent>(e => _log.Add("stopping:" + e.HostName));
        _events = context.Events;

        // Registries: a host-declared one, a missing one, entry ids, and the name rules.
        var commands = context.Registries.Get(CommonRegistries.Commands);
        _commands = commands;
        var entry = commands.Register("tour", new ModCommand("tour", "Runs the API tour checks.", Run) { OperatorOnly = false });
        Check("registry", entry.Id == "api-tour:tour" && entry.OwnerModId == "api-tour" && commands.Key.Name == "spiralloader:commands"
            && commands.Entries.Contains(entry) && !context.Registries.TryGet(s_notes, out var missing) && missing is null
            && RegistryNames.IsValidQualifiedName("a:b") && !RegistryNames.IsValidPath("Bad Name"));
        try {
            context.Registries.Get(s_notes);
            Check("registry.missing-throws", false);
        } catch (KeyNotFoundException) {
        }

        // Versions and ranges.
        Check("versioning", VersionRange.Parse("^1.2.0 || >=3.0.0 <4.0.0").IsSatisfiedBy(new SemanticVersion(1, 5, 0))
            && !VersionRange.Parse("^1.2.0").IsSatisfiedBy(new SemanticVersion(2, 0, 0))
            && VersionRange.Any.IsSatisfiedBy(new SemanticVersion(9, 9, 9))
            && SemanticVersion.Parse("1.0.0-alpha.1") < SemanticVersion.Parse("1.0.0")
            && !SemanticVersion.TryParse("nope", out _));

        // Public records and the inert null implementations.
        Check("records", new ModDependency("x", VersionRange.Any).ModId == "x"
            && new AssetDeclaration(AssetKind.Wad, "a", "b").Kind == AssetKind.Wad
            && !new ModCommandInvocation(["a"], "me").CallerIsOperator);
        Check("null-implementations", !NullEventBus.Instance.HasSubscribers<TourEvent>() && !EmptyRegistryProvider.Instance.TryGet(s_notes, out _));
        try {
            NullEventBus.Instance.Subscribe<TourEvent>(_ => { });
            Check("null-bus-throws", false);
        } catch (RegistrationClosedException) {
        }
    }

    private readonly List<string> _order = [];
    private IEventBus? _events;
    private IRegistry<ModCommand>? _commands;

    // Runs after the host froze the bus and the registries.
    private string? Run(ModCommandInvocation invocation) {
        var first = new TourEvent();
        _events!.Publish(first);
        Check("events.order", first.Value == 11 && !first.IsCancelled && string.Join(",", _order) == "high,normal,low,monitor");

        _order.Clear();
        var vetoed = new TourEvent { Value = 95 };
        _events.Publish(vetoed);
        Check("events.cancel", vetoed.IsCancelled && vetoed.Value == 106 && string.Join(",", _order) == "high,normal,monitor");

        try {
            _events.Subscribe<TourEvent>(_ => { });
            Check("events.frozen", false);
        } catch (RegistrationClosedException) {
        }
        try {
            _commands!.Register("late", new ModCommand("late", "too late", _ => null));
            Check("registry.frozen", false);
        } catch (RegistrationClosedException) {
        }

        return _failures.Count == 0
            ? $"tour ok; lifecycle: {string.Join(" ", _log)}"
            : "tour FAILED: " + string.Join("; ", _failures);
    }

    private void Check(string name, bool ok) {
        if (!ok) {
            _failures.Add(name);
        }
    }

    public void Shutdown() => _log.Add("shutdown");

}

/// <summary>A second entrypoint that relies on the default implementation of <see cref="ISpiralMod.Shutdown"/>.</summary>
public sealed class MinimalMod : ISpiralMod {

    public void Initialize(IModContext context) => context.Logger.Info("minimal entrypoint initialized");

}
