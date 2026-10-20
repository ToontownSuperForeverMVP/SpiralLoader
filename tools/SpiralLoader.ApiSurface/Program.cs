using System.Reflection;
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
        if (args.Length < 2 || args[0] is not ("dump" or "check") || (args[0] == "check" && args.Length < 3)) {
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

        Assembly assembly;
        try {
            assembly = Assembly.LoadFrom(Path.GetFullPath(positional[0]));
        } catch (Exception ex) when (ex is IOException or BadImageFormatException) {
            Console.Error.WriteLine($"Cannot load {positional[0]}: {ex.Message}");
            return 2;
        }

        var surface = new List<string> { "surface " + (label ?? assembly.GetName().Name + " " + assembly.GetName().Version) };
        if (typeNames.Count > 0) {
            var types = typeNames.Select(name => assembly.GetType(name) ?? throw new ArgumentException($"{name} is not a type of {assembly.GetName().Name}.")).ToList();
            surface.AddRange(PublicApiSurface.Describe(types));
        } else {
            surface.AddRange(PublicApiSurface.Describe(assembly, prefix));
        }

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
    }

}
