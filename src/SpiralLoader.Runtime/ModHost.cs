using System.Reflection;
using SpiralLoader.Api;
using SpiralLoader.Api.Events;
using SpiralLoader.Api.Logging;
using SpiralLoader.Api.Manifest;
using SpiralLoader.Api.Registries;
using SpiralLoader.Api.Versioning;
using SpiralLoader.Runtime.Assets;
using SpiralLoader.Runtime.Events;
using SpiralLoader.Runtime.Logging;
using SpiralLoader.Runtime.Manifest;
using SpiralLoader.Runtime.Registries;
using SpiralLoader.Runtime.Resolution;

namespace SpiralLoader.Runtime;

/// <summary>
/// Discovers, validates, orders, loads and initializes mods, then owns them for the life of the process.
/// </summary>
/// <remarks>
/// Typical host integration:
/// <code>
/// var host = new ModHost(options);
/// host.AddRegistry(MyRegistries.Services, ValidateService);   // before Load
/// host.Load();                                                 // throws ModLoadException under AbortStartup
/// host.Events.Publish(new ServerStartingEvent("MyHost"));
/// ...
/// host.Shutdown();
/// </code>
/// <see cref="Load"/> may be called once. Mods are never unloaded; changing them requires a restart.
/// </remarks>
public sealed class ModHost : IModQuery {

    /// <summary>The owner name used for subscriptions and registrations made by the host itself.</summary>
    public const string HostOwner = "host";

    private readonly ModHostOptions _options;
    private readonly IModLogger _logger;
    private readonly EventBus _bus;
    private readonly RegistryStore _registries = new();
    private readonly Dictionary<string, BuiltInMod> _builtIns = new(StringComparer.Ordinal);
    private readonly SharedAssemblyPolicy _sharedAssemblies;
    private readonly List<ModDiagnostic> _hostDiagnostics = [];
    private List<ModRecord> _records = [];
    private int _state; // 0 = new, 1 = loading/loaded, 2 = shut down

    public ModHost(ModHostOptions options) {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _logger = CreateLogger(SpiralLoaderInfo.LoaderModId);
        _bus = new EventBus(_logger, options.HandlerFaultPolicy);
        _sharedAssemblies = new SharedAssemblyPolicy(options.SharedAssemblies);
        Events = new ScopedEventBus(_bus, HostOwner, -1);
        Registries = new ScopedRegistryProvider(_registries, HostOwner);

        AddBuiltIn(new BuiltInMod(SpiralLoaderInfo.LoaderModId, SpiralLoaderInfo.ApiVersion, "SpiralLoader mod API"));
        var runtime = Environment.Version;
        AddBuiltIn(new BuiltInMod("dotnet", new SemanticVersion(runtime.Major, runtime.Minor, Math.Max(runtime.Build, 0)), ".NET runtime"));
        foreach (var builtIn in options.BuiltIns) {
            AddBuiltIn(builtIn);
        }
    }

    /// <summary>The host's view of the event bus (owner <see cref="HostOwner"/>).</summary>
    public IEventBus Events { get; }

    /// <summary>The host's view of the registries (owner <see cref="HostOwner"/>).</summary>
    public IRegistryProvider Registries { get; }

    /// <summary>A logger attributed to the loader, for host integration code.</summary>
    public IModLogger Logger => _logger;

    public IReadOnlyDictionary<string, BuiltInMod> BuiltIns => _builtIns;

    /// <summary>Event handler exceptions caught so far, by owner.</summary>
    public IReadOnlyDictionary<string, int> HandlerFaults => _bus.FaultCounts;

    /// <summary>The report from <see cref="Load"/>, or null before it has run.</summary>
    public ModLoadReport? Report { get; private set; }

    /// <summary>Declares a registry mods can contribute to. Must be called before <see cref="Load"/>.</summary>
    public void AddRegistry<T>(RegistryKey<T> key, RegistryValidator<T>? validator = null) where T : notnull {
        if (Volatile.Read(ref _state) != 0) {
            throw new InvalidOperationException("Registries must be declared before Load().");
        }
        _registries.Add(key, validator);
    }

    /// <summary>
    /// Runs the full load pipeline. Returns the report; under <see cref="ModFailurePolicy.AbortStartup"/>
    /// throws <see cref="ModLoadException"/> if any mod was rejected or failed.
    /// </summary>
    public ModLoadReport Load() {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) {
            throw new InvalidOperationException("ModHost.Load() can only be called once per process.");
        }

        _logger.Info($"SpiralLoader {SpiralLoaderInfo.ApiVersion} starting (host: {_options.HostName}, profile: {_options.Profile}, entrypoint: {_options.EntrypointKind}).");
        _records = Discover();
        ParseManifests(_records);
        ApplyConfigurationFilters(_records);
        ValidateFiles(_records);
        ValidateCompatibility(_records);
        var ordered = DependencyResolver.Resolve(_records, _builtIns);
        for (var i = 0; i < ordered.Count; i++) {
            ordered[i].LoadIndex = i;
        }

        foreach (var record in ordered) {
            InitializeMod(record);
        }

        _bus.Freeze();
        _registries.Freeze();

        var reportOrder = ordered
            .Concat(_records.Except(ordered).OrderBy(r => r.Id, StringComparer.Ordinal))
            .ToList();
        Report = new ModLoadReport(reportOrder, [.. _hostDiagnostics]);
        LogReport(Report);

        if (Report.HasFailures && _options.FailurePolicy == ModFailurePolicy.AbortStartup) {
            ShutdownMods();
            throw new ModLoadException(Report);
        }

        Events.Publish(new ModsLoadedEvent(Report.LoadOrder));
        return Report;
    }

    /// <summary>Calls <see cref="ISpiralMod.Shutdown"/> on every initialized mod in reverse load order. Idempotent.</summary>
    public void Shutdown() {
        if (Interlocked.Exchange(ref _state, 2) == 2) {
            return;
        }
        ShutdownMods();
    }

    /// <summary>Plans asset overlays for the initialized mods. Valid after <see cref="Load"/>.</summary>
    public AssetOverlayPlan PlanAssets()
        => AssetOverlayPlanner.Plan((Report ?? throw new InvalidOperationException("Call Load() first.")).Mods
            .Where(m => m.State == ModState.Initialized));

    public bool IsLoaded(string modId)
        => _builtIns.ContainsKey(modId) || _records.Any(r => r.Id == modId && r.State == ModState.Initialized);

    public ModManifest? GetManifest(string modId)
        => _records.FirstOrDefault(r => r.Id == modId && r.State == ModState.Initialized)?.Manifest;

    public IReadOnlyList<string> LoadOrder
        => [.. _records.Where(r => r.State == ModState.Initialized).OrderBy(r => r.LoadIndex).Select(r => r.Id)];

    private void AddBuiltIn(BuiltInMod builtIn) {
        if (!ManifestParser.IsValidModId(builtIn.Id)) {
            throw new ArgumentException($"Built-in id '{builtIn.Id}' is not a valid mod id.");
        }
        _builtIns[builtIn.Id] = builtIn;
    }

    private List<ModRecord> Discover() {
        var records = new List<ModRecord>();
        foreach (var root in _options.ModDirectories.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal)) {
            if (!Directory.Exists(root)) {
                _hostDiagnostics.Add(new ModDiagnostic(DiagnosticSeverity.Info, "host.mods_directory_missing",
                    $"Mods directory {root} does not exist; no mods loaded from it."));
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal)) {
                var name = Path.GetFileName(directory);
                if (name.StartsWith('.')) {
                    continue;
                }

                var manifestPath = Path.Combine(directory, SpiralLoaderInfo.ManifestFileName);
                if (!File.Exists(manifestPath)) {
                    _logger.Debug($"Skipping {directory}: no {SpiralLoaderInfo.ManifestFileName}.");
                    continue;
                }
                records.Add(new ModRecord(directory, manifestPath));
            }
        }
        _logger.Debug($"Discovered {records.Count} mod director{(records.Count == 1 ? "y" : "ies")}.");
        return records;
    }

    private static void ParseManifests(List<ModRecord> records) {
        foreach (var record in records) {
            var result = ManifestParser.ParseFile(record.ManifestPath);
            record.AddRange(result.Diagnostics);
            record.Manifest = result.Manifest;
            if (!result.Succeeded) {
                record.State = ModState.Rejected;
            }
        }
    }

    private void ApplyConfigurationFilters(List<ModRecord> records) {
        foreach (var record in records.Where(r => r.State == ModState.Discovered)) {
            if (_options.DisabledMods.Contains(record.Id)) {
                record.Add(new ModDiagnostic(DiagnosticSeverity.Info, DiagnosticCodes.ModDisabled, "Disabled by configuration."));
                record.State = ModState.Disabled;
            } else if (_builtIns.ContainsKey(record.Id)) {
                record.Reject(DiagnosticCodes.ModReservedId, $"Mod id '{record.Id}' is reserved for a host built-in.");
            }
        }

        foreach (var group in records.Where(r => r.State == ModState.Discovered).GroupBy(r => r.Id, StringComparer.Ordinal)) {
            if (group.Count() < 2) {
                continue;
            }
            var locations = string.Join(", ", group.Select(r => r.Directory));
            foreach (var record in group) {
                record.Reject(DiagnosticCodes.ModDuplicateId, $"Mod id '{record.Id}' is provided by more than one directory: {locations}.");
            }
        }
    }

    private static void ValidateFiles(List<ModRecord> records) {
        foreach (var record in records.Where(r => r.State == ModState.Discovered)) {
            var manifest = record.Manifest!;
            foreach (var assembly in manifest.Assemblies) {
                if (!File.Exists(Path.Combine(record.Directory, assembly))) {
                    record.Reject(DiagnosticCodes.AssemblyMissing, $"Declared assembly '{assembly}' does not exist.");
                }
            }
            foreach (var asset in manifest.Assets) {
                if (!Directory.Exists(Path.Combine(record.Directory, asset.Source))) {
                    record.Reject(DiagnosticCodes.AssetMissing, $"Declared {asset.Kind} asset directory '{asset.Source}' does not exist.");
                }
            }
        }
    }

    private void ValidateCompatibility(List<ModRecord> records) {
        foreach (var record in records.Where(r => r.State == ModState.Discovered)) {
            var compatibility = record.Manifest!.Compatibility;
            CheckPin(record, compatibility.ImlightCommits, _options.ImlightCommit, "Imlight commit",
                DiagnosticCodes.CompatibilityImlightCommit, StringComparer.OrdinalIgnoreCase);
            CheckPin(record, compatibility.ClientRevisions, _options.ClientRevision, "client revision",
                DiagnosticCodes.CompatibilityClientRevision, StringComparer.Ordinal);
        }
    }

    private void CheckPin(ModRecord record, IReadOnlyList<string> supported, string? actual, string what, string code,
                          StringComparer comparer) {
        if (supported.Count == 0 || record.State != ModState.Discovered) {
            return;
        }

        var list = string.Join(", ", supported);
        if (actual is null) {
            record.Warn(code, $"Mod is pinned to {what} {list}, but the host does not report one; compatibility is unverified.");
            return;
        }
        if (supported.Contains(actual, comparer)) {
            return;
        }

        var message = $"Mod supports {what} {list}, but the host is {actual}.";
        if (_options.CompatibilityMode == CompatibilityMode.Strict) {
            record.Reject(code, message);
        } else {
            record.Warn(code, message + " Loading anyway (compatibility mode: WarnOnly).");
        }
    }

    private void InitializeMod(ModRecord record) {
        var manifest = record.Manifest!;
        var byId = _records.Where(r => r.Manifest is not null).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First());

        // A required mod dependency that failed at load or init time fails this mod too.
        var failedDependency = manifest.Depends
            .Select(d => byId.GetValueOrDefault(d.ModId))
            .FirstOrDefault(d => d is not null && d.State != ModState.Initialized);
        if (failedDependency is not null) {
            record.Fail(DiagnosticCodes.DependencyFailed, $"Required dependency '{failedDependency.Id}' did not initialize ({failedDependency.State}).");
            return;
        }

        var dependencyContexts = manifest.Depends.Concat(manifest.Recommends)
            .Select(d => byId.GetValueOrDefault(d.ModId))
            .Where(d => d is { State: ModState.Initialized, LoadContext: not null })
            .Select(d => d!.LoadContext!)
            .Distinct()
            .ToList();

        var context = new ModLoadContext(record.Id, record.Directory, _sharedAssemblies, dependencyContexts,
            sharedName => record.Warn(DiagnosticCodes.AssemblySharedCopy,
                $"Ships its own copy of {sharedName}; the host's copy is used instead."));
        record.LoadContext = context;

        if (!TryLoadAssemblies(record, context) || !TryCreateEntrypoints(record)) {
            return;
        }
        record.State = ModState.Loaded;

        var modContext = new ModContext(
            manifest,
            CreateLogger(record.Id),
            new ScopedEventBus(_bus, record.Id, record.LoadIndex),
            new ScopedRegistryProvider(_registries, record.Id),
            new ModEnvironment(
                record.Directory,
                Path.Combine(_options.DataRoot, _options.Profile, record.Id),
                Path.Combine(_options.ConfigRoot, record.Id),
                _options.Profile,
                _options.HostName,
                _options.EntrypointKind),
            this);

        foreach (var entrypoint in record.Entrypoints) {
            try {
                entrypoint.Initialize(modContext);
            } catch (Exception ex) {
                record.Fail(DiagnosticCodes.InitializationFailed, $"{entrypoint.GetType().FullName}.Initialize threw {ex.GetType().Name}: {ex.Message}");
                _logger.Error($"Mod '{record.Id}' failed during initialization.", ex);
                RollBack(record);
                return;
            }
        }

        record.State = ModState.Initialized;
        _logger.Info($"Initialized {manifest.Id} {manifest.Version} ({record.Entrypoints.Count} entrypoint(s)).");
    }

    private bool TryLoadAssemblies(ModRecord record, ModLoadContext context) {
        var loaded = new List<Assembly>();
        foreach (var relative in record.Manifest!.Assemblies) {
            var path = Path.Combine(record.Directory, relative);
            try {
                var assembly = context.LoadDeclared(path);
                // Force every type to resolve now, so a missing reference fails this mod with a clear message
                // instead of failing whichever host code reflects over loaded assemblies later.
                _ = assembly.GetTypes();
                loaded.Add(assembly);
            } catch (ReflectionTypeLoadException ex) {
                var reasons = ex.LoaderExceptions.Where(e => e is not null).Select(e => e!.Message).Distinct().Take(5);
                record.Fail(DiagnosticCodes.AssemblyLoadFailed, $"Types in '{relative}' could not be loaded: {string.Join(" | ", reasons)}");
                return false;
            } catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or FileNotFoundException or IOException) {
                record.Fail(DiagnosticCodes.AssemblyLoadFailed, $"Could not load '{relative}': {ex.Message}");
                return false;
            }
        }
        record.Assemblies = loaded;
        return true;
    }

    private bool TryCreateEntrypoints(ModRecord record) {
        foreach (var typeName in record.Manifest!.GetEntrypoints(_options.EntrypointKind)) {
            var type = record.Assemblies.Select(a => a.GetType(typeName, throwOnError: false)).FirstOrDefault(t => t is not null);
            if (type is null) {
                record.Fail(DiagnosticCodes.EntrypointNotFound, $"Entrypoint type '{typeName}' was not found in the mod's assemblies.");
                return false;
            }

            var problem = DescribeInvalidEntrypoint(type);
            if (problem is not null) {
                record.Fail(DiagnosticCodes.EntrypointInvalid, $"Entrypoint '{typeName}' {problem}");
                return false;
            }

            try {
                record.Entrypoints.Add((ISpiralMod) Activator.CreateInstance(type)!);
            } catch (TargetInvocationException ex) when (ex.InnerException is not null) {
                record.Fail(DiagnosticCodes.InitializationFailed, $"Constructor of '{typeName}' threw {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                return false;
            }
        }
        return true;
    }

    private static string? DescribeInvalidEntrypoint(Type type) {
        if (!typeof(ISpiralMod).IsAssignableFrom(type)) {
            var foreign = type.GetInterfaces().FirstOrDefault(i => i.FullName == typeof(ISpiralMod).FullName);
            return foreign is not null
                ? $"implements ISpiralMod from a different SpiralLoader.Api ({foreign.Assembly.Location}). Reference SpiralLoader.Api with Private=false."
                : "does not implement SpiralLoader.Api.ISpiralMod.";
        }
        if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters) {
            return "must be a non-abstract, non-generic class.";
        }
        if (type.GetConstructor(Type.EmptyTypes) is null) {
            return "must have a public parameterless constructor.";
        }
        return null;
    }

    private void RollBack(ModRecord record) {
        var subscriptions = _bus.RemoveOwner(record.Id);
        var registrations = _registries.RemoveOwner(record.Id);
        if (subscriptions + registrations > 0) {
            _logger.Warn($"Rolled back {subscriptions} subscription(s) and {registrations} registration(s) made by '{record.Id}'.");
        }
    }

    private void ShutdownMods() {
        foreach (var record in _records.Where(r => r.State == ModState.Initialized).OrderByDescending(r => r.LoadIndex)) {
            foreach (var entrypoint in Enumerable.Reverse(record.Entrypoints)) {
                try {
                    entrypoint.Shutdown();
                } catch (Exception ex) {
                    _logger.Error($"Mod '{record.Id}' threw during shutdown; ignored.", ex);
                }
            }
            record.State = ModState.ShutDown;
        }
    }

    private void LogReport(ModLoadReport report) {
        var level = report.HasFailures ? ModLogLevel.Error : ModLogLevel.Information;
        _logger.Log(level, report.Format());
    }

    private IModLogger CreateLogger(string source) => new SinkLogger(source, _options.LogSink, _options.MinimumLogLevel);

}
