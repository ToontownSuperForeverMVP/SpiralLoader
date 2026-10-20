using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using SpiralLoader.Tooling;

namespace SpiralLoader.Tooling;

/// <summary>
/// <c>dump</c> prints the public API surface of an assembly; <c>check</c> compares it with a baseline file.
/// Exit codes: 0 identical (or additions only), 1 members removed or changed, 2 usage or load error.
/// </summary>
internal static class Program {

    private const string Usage = """
        Usage:
          SpiralLoader.ApiSurface dump  <assembly.dll> [--namespace <prefix> | --type <full name>]... [--label <text>]
          SpiralLoader.ApiSurface check <assembly.dll> <baseline.txt> [--namespace <prefix> | --type <full name>]... [--label <text>] [--strict]

        dump   Prints one sorted line per public type and member (the baseline format).
        check  Compares the assembly with a baseline. Removed or changed lines are potentially breaking and make the
               exit code 1. Additions alone exit 0, or 1 with --strict (use --strict in CI to force a deliberate
               baseline update, and a minor version bump, whenever the API grows).
        --type selects individual types instead of a namespace (for example SpiralLoader.Runtime.DiagnosticCodes).
        --label is written as the first line ("surface <text>"); the default is the assembly's name and version.
        """;

    private static int Main(string[] args) {
        if (args.Length < 2 || args[0] is not ("dump" or "check")) {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        string? prefix = null;
        var typeNames = new List<string>();
        string? label = null;
        var strict = false;
        var positional = new List<string>();
        for (var i = 1; i < args.Length; i++) {
            switch (args[i]) {
                case "--namespace" when i + 1 < args.Length: prefix = args[++i]; break;
                case "--type" when i + 1 < args.Length: typeNames.Add(args[++i]); break;
                case "--label" when i + 1 < args.Length: label = args[++i]; break;
                case "--strict": strict = true; break;
                default: positional.Add(args[i]); break;
            }
        }

        if (positional.Count < (args[0] == "check" ? 2 : 1)) {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        Assembly assembly;
        var assemblyPath = Path.GetFullPath(positional[0]);
        try {
            var resolver = new AssemblyDependencyResolver(assemblyPath);
            var assemblyDirectory = Path.GetDirectoryName(assemblyPath)!;
            var packageAssets = GetPackageAssets(assemblyPath);
            AssemblyLoadContext.Default.Resolving += (_, name) => {
                var resolved = resolver.ResolveAssemblyToPath(name);
                if (resolved is not null) {
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(resolved);
                }

                var sibling = Path.Combine(assemblyDirectory, name.Name + ".dll");
                if (File.Exists(sibling)) {
                    return AssemblyLoadContext.Default.LoadFromAssemblyPath(sibling);
                }

                return packageAssets.TryGetValue(name.Name + ".dll", out var packageAsset)
                    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(packageAsset)
                    : null;
            };
            assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);

            var surface = Describe(assembly, typeNames, prefix, label);
            if (args[0] == "dump") {
                foreach (var line in surface) {
                    Console.WriteLine(line);
                }
                return 0;
            }

            var baseline = File.ReadAllLines(positional[1]).Where(line => line.Length > 0).ToList();
            var comparison = new ApiComparison(baseline, surface);
            Console.WriteLine(comparison.Format(positional[1]));
            return comparison.IsBreaking || (strict && !comparison.IsIdentical) ? 1 : 0;
        } catch (Exception ex) {
            Console.Error.WriteLine($"Cannot inspect {positional[0]}: {DescribeFailure(ex)}");
            return 2;
        }
    }

    private static List<string> Describe(Assembly assembly, IReadOnlyList<string> typeNames, string? prefix, string? label) {
        var surface = new List<string> { "surface " + (label ?? assembly.GetName().Name + " " + assembly.GetName().Version) };
        if (typeNames.Count > 0) {
            var types = typeNames.Select(name => assembly.GetType(name) ?? throw new ArgumentException($"{name} is not a type of {assembly.GetName().Name}.")).ToList();
            surface.AddRange(PublicApiSurface.Describe(types));
        } else {
            surface.AddRange(PublicApiSurface.Describe(assembly, prefix));
        }
        return surface;
    }

    private static string DescribeFailure(Exception exception) {
        if (exception is ReflectionTypeLoadException typeLoadException) {
            var loaderErrors = typeLoadException.LoaderExceptions.Where(error => error is not null)
                .Select(error => error!.Message).Distinct(StringComparer.Ordinal);
            return "one or more dependencies could not be loaded: " + string.Join("; ", loaderErrors);
        }
        return exception.Message;
    }

    private static Dictionary<string, string> GetPackageAssets(string assemblyPath) {
        var depsPath = Path.ChangeExtension(assemblyPath, ".deps.json");
        if (!File.Exists(depsPath)) {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(depsPath));
        var root = document.RootElement;
        if (!root.TryGetProperty("runtimeTarget", out var runtimeTarget)
            || !runtimeTarget.TryGetProperty("name", out var targetName)
            || !root.TryGetProperty("targets", out var targets)
            || !targets.TryGetProperty(targetName.GetString()!, out var target)
            || !root.TryGetProperty("libraries", out var libraries)) {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var folders = root.TryGetProperty("packageFolders", out var packageFolders)
            ? packageFolders.EnumerateObject().Select(folder => folder.Name).ToList()
            : [];
        var configuredPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrWhiteSpace(configuredPackages)) {
            folders.Add(configuredPackages);
        }
        var defaultPackages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        if (Directory.Exists(defaultPackages)) {
            folders.Add(defaultPackages);
        }
        folders = folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in target.EnumerateObject()) {
            if (!libraries.TryGetProperty(library.Name, out var metadata)
                || !metadata.TryGetProperty("type", out var type)
                || !string.Equals(type.GetString(), "package", StringComparison.OrdinalIgnoreCase)
                || !metadata.TryGetProperty("path", out var packagePath)) {
                continue;
            }

            foreach (var groupName in new[] { "runtime", "runtimeTargets" }) {
                if (!library.Value.TryGetProperty(groupName, out var runtimeGroup)) {
                    continue;
                }

                foreach (var asset in runtimeGroup.EnumerateObject()) {
                    var fileName = Path.GetFileName(asset.Name);
                    foreach (var folder in folders) {
                        var candidate = Path.GetFullPath(Path.Combine(folder, packagePath.GetString()!, asset.Name.Replace('/', Path.DirectorySeparatorChar)));
                        if (File.Exists(candidate)) {
                            assets.TryAdd(fileName, candidate);
                            break;
                        }
                    }
                }
            }
        }
        return assets;
    }

}
