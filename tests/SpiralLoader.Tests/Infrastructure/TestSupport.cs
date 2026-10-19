using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SpiralLoader.Api;
using SpiralLoader.Api.Logging;
using SpiralLoader.Runtime;
using SpiralLoader.Runtime.Events;
using SpiralLoader.Runtime.Logging;

namespace SpiralLoader.Tests.Infrastructure;

/// <summary>A temporary directory deleted on dispose.</summary>
public sealed class TempDirectory : IDisposable {

    public TempDirectory() {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "spiralloader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose() {
        try {
            Directory.Delete(Path, recursive: true);
        } catch (IOException) {
            // Loaded mod assemblies stay mapped for the life of the process (non-collectible contexts).
        } catch (UnauthorizedAccessException) {
        }
    }

}

/// <summary>
/// Creates mod directories under a mods root: a manifest plus, optionally, an assembly compiled from C# source.
/// </summary>
public sealed class ModWorkspace : IDisposable {

    private readonly TempDirectory _root = new();

    public ModWorkspace() {
        Directory.CreateDirectory(ModsDirectory);
    }

    public string ModsDirectory => _root.Combine("mods");

    public string DataRoot => _root.Combine("data");

    public string ConfigRoot => _root.Combine("config");

    public MemoryLogSink Log { get; } = new();

    public string ModDirectory(string id) => System.IO.Path.Combine(ModsDirectory, id);

    /// <summary>Writes a manifest. <paramref name="manifest"/> is serialized as-is (anonymous objects work).</summary>
    public string AddManifest(string directoryName, object manifest) {
        var directory = ModDirectory(directoryName);
        Directory.CreateDirectory(directory);
        var json = manifest as string ?? JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(System.IO.Path.Combine(directory, SpiralLoaderInfo.ManifestFileName), json);
        return directory;
    }

    /// <summary>
    /// Writes a minimal valid manifest for a mod without code. Extra fields are merged in.
    /// </summary>
    public string AddDataMod(string id, string version = "1.0.0", Dictionary<string, object?>? extra = null) {
        var manifest = new Dictionary<string, object?> {
            ["schemaVersion"] = 1,
            ["id"] = id,
            ["version"] = version,
        };
        foreach (var (key, value) in extra ?? []) {
            manifest[key] = value;
        }
        return AddManifest(id, manifest);
    }

    /// <summary>
    /// Compiles <paramref name="source"/> into <c>{assemblyName}.dll</c> inside the mod directory and writes a
    /// manifest declaring it with the given server entrypoints.
    /// </summary>
    public string AddCodeMod(string id, string assemblyName, string source, string[] entrypoints,
                             Dictionary<string, object?>? extra = null, IEnumerable<string>? references = null,
                             string version = "1.0.0") {
        var directory = ModDirectory(id);
        Directory.CreateDirectory(directory);
        TestCompiler.Compile(assemblyName, source, System.IO.Path.Combine(directory, assemblyName + ".dll"), references);
        var manifest = new Dictionary<string, object?> {
            ["schemaVersion"] = 1,
            ["id"] = id,
            ["version"] = version,
            ["assemblies"] = new[] { assemblyName + ".dll" },
            ["entrypoints"] = new Dictionary<string, object> { ["server"] = entrypoints },
        };
        foreach (var (key, value) in extra ?? []) {
            manifest[key] = value;
        }
        AddManifest(id, manifest);
        return directory;
    }

    public ModHostOptions Options(Action<ModHostOptions>? configure = null) {
        var options = new ModHostOptions {
            HostName = "TestHost",
            DataRoot = DataRoot,
            ConfigRoot = ConfigRoot,
            LogSink = Log,
            MinimumLogLevel = ModLogLevel.Debug,
            FailurePolicy = ModFailurePolicy.ContinueWithoutFailedMods,
            HandlerFaultPolicy = HandlerFaultPolicy.Isolate,
        };
        options.ModDirectories.Add(ModsDirectory);
        configure?.Invoke(options);
        return options;
    }

    public ModHost CreateHost(Action<ModHostOptions>? configure = null) => new(Options(configure));

    public ModLoadReport Load(Action<ModHostOptions>? configure = null) => CreateHost(configure).Load();

    public void Dispose() => _root.Dispose();

}

/// <summary>Compiles C# source into a real assembly referencing the running framework and SpiralLoader.Api.</summary>
public static class TestCompiler {

    private static readonly Lazy<List<MetadataReference>> s_frameworkReferences = new(() => {
        var tpa = (string) AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        return tpa.Split(System.IO.Path.PathSeparator)
            .Where(path => System.IO.Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal)
                           || System.IO.Path.GetFileName(path) is "mscorlib.dll" or "netstandard.dll" or "Microsoft.Win32.Primitives.dll")
            .Select(path => (MetadataReference) MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(ISpiralMod).Assembly.Location))
            .ToList();
    });

    public static void Compile(string assemblyName, string source, string outputPath, IEnumerable<string>? references = null) {
        var syntax = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntax],
            s_frameworkReferences.Value.Concat((references ?? []).Select(path => MetadataReference.CreateFromFile(path))),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var result = compilation.Emit(outputPath);
        if (!result.Success) {
            File.Delete(outputPath);
            var errors = result.Diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
            throw new InvalidOperationException($"Test mod '{assemblyName}' did not compile:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
        }
    }

}

/// <summary>Values the build bakes into the test assembly.</summary>
public static class BuildPaths {

    public static string ExampleModsDirectory => Metadata("ExampleModsDirectory");

    public static string HeadlessHostPath => Metadata("HeadlessHostPath");

    private static string Metadata(string key)
        => typeof(BuildPaths).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;

}

/// <summary>
/// Records calls from compiled test mods. Test mods reach it by reflection-free static access through a
/// shared assembly: this test assembly is on the host's TPA list, so mods resolve it from the default context.
/// </summary>
public static class TestJournal {

    private static readonly Lock s_gate = new();
    private static readonly Dictionary<string, List<string>> s_entries = [];

    public static void Record(string channel, string entry) {
        lock (s_gate) {
            if (!s_entries.TryGetValue(channel, out var list)) {
                list = [];
                s_entries[channel] = list;
            }
            list.Add(entry);
        }
    }

    public static IReadOnlyList<string> Read(string channel) {
        lock (s_gate) {
            return s_entries.TryGetValue(channel, out var list) ? [.. list] : [];
        }
    }

    public static string NewChannel() => Guid.NewGuid().ToString("N");

    public static string AssemblyPath => typeof(TestJournal).Assembly.Location;

}
