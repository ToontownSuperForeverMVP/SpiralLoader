using SpiralLoader.Api.Manifest;

namespace SpiralLoader.Runtime.Assets;

/// <summary>One file a mod contributes to a client archive or to SpiralDB.</summary>
/// <param name="Kind">Where the file goes.</param>
/// <param name="Target">The archive (for example <c>GameData/Root.wad</c>) or SpiralDB collection directory.</param>
/// <param name="EntryPath">The entry path inside the target, with '/' separators.</param>
/// <param name="ModId">The mod whose file wins.</param>
/// <param name="SourceFile">Absolute path of the winning file.</param>
/// <param name="Overrides">Mods (in load order) whose contribution to the same entry this one replaces.</param>
public sealed record AssetOverride(AssetKind Kind, string Target, string EntryPath, string ModId, string SourceFile,
                                   IReadOnlyList<string> Overrides);

/// <summary>Two mods contributing the same entry without an ordering relationship between them.</summary>
public sealed record AssetConflict(AssetKind Kind, string Target, string EntryPath, string FirstModId, string SecondModId) {

    public override string ToString()
        => $"{Kind} {Target}:{EntryPath} is provided by both '{FirstModId}' and '{SecondModId}', and neither declares an ordering on the other.";

}

/// <summary>The merged view of every mod's declared assets.</summary>
public sealed class AssetOverlayPlan {

    internal AssetOverlayPlan(IReadOnlyList<AssetOverride> overrides, IReadOnlyList<AssetConflict> conflicts) {
        Overrides = overrides;
        Conflicts = conflicts;
    }

    /// <summary>Winning contributions, ordered by kind, target and entry path.</summary>
    public IReadOnlyList<AssetOverride> Overrides { get; }

    public IReadOnlyList<AssetConflict> Conflicts { get; }

    public bool HasConflicts => Conflicts.Count > 0;

    /// <summary>The overrides for one client archive.</summary>
    public IEnumerable<AssetOverride> ForArchive(string target)
        => Overrides.Where(o => o.Kind == AssetKind.Wad && string.Equals(o.Target, target, StringComparison.OrdinalIgnoreCase));

}

/// <summary>
/// Computes which mod's file wins for every archive entry and SpiralDB file. This milestone only plans;
/// writing archives and patch metadata is future work (see docs/next-milestone.md).
/// </summary>
/// <remarks>
/// Later mods in load order win. That is only accepted silently when the later mod explicitly ordered itself
/// after the earlier one (directly or transitively, through <c>depends</c>, <c>recommends</c>, <c>loadAfter</c>
/// or the earlier mod's <c>loadBefore</c>); otherwise the overlap is reported as a conflict, because the outcome
/// would depend on mod ids rather than on intent. Entry paths compare case-insensitively.
/// </remarks>
public static class AssetOverlayPlanner {

    public static AssetOverlayPlan Plan(IEnumerable<ModRecord> modsInLoadOrder) {
        var mods = modsInLoadOrder.Where(m => m.Manifest is not null).ToList();
        var orderedAfter = BuildOrderingClosure(mods);
        var winners = new Dictionary<(AssetKind, string, string), (AssetOverride Override, List<string> Replaced)>();
        var conflicts = new List<AssetConflict>();

        foreach (var mod in mods) {
            foreach (var declaration in mod.Manifest!.Assets) {
                var root = Path.Combine(mod.Directory, declaration.Source);
                if (!Directory.Exists(root)) {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)) {
                    var entry = Path.GetRelativePath(root, file).Replace('\\', '/');
                    var key = (declaration.Kind, declaration.Target.ToLowerInvariant(), entry.ToLowerInvariant());
                    var contribution = new AssetOverride(declaration.Kind, declaration.Target, entry, mod.Id, Path.GetFullPath(file), []);

                    if (winners.TryGetValue(key, out var existing)) {
                        if (existing.Override.ModId != mod.Id && !orderedAfter[mod.Id].Contains(existing.Override.ModId)) {
                            conflicts.Add(new AssetConflict(declaration.Kind, declaration.Target, entry, existing.Override.ModId, mod.Id));
                        }
                        existing.Replaced.Add(existing.Override.ModId);
                        winners[key] = (contribution, existing.Replaced);
                    } else {
                        winners[key] = (contribution, []);
                    }
                }
            }
        }

        var overrides = winners.Values
            .Select(w => w.Override with { Overrides = w.Replaced.Distinct().ToList() })
            .OrderBy(o => o.Kind)
            .ThenBy(o => o.Target, StringComparer.OrdinalIgnoreCase)
            .ThenBy(o => o.EntryPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new AssetOverlayPlan(overrides, conflicts);
    }

    // orderedAfter[x] = every mod x has explicitly asked to come after.
    private static Dictionary<string, HashSet<string>> BuildOrderingClosure(List<ModRecord> mods) {
        var direct = mods.ToDictionary(m => m.Id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var mod in mods) {
            var manifest = mod.Manifest!;
            foreach (var id in manifest.Depends.Concat(manifest.Recommends).Select(d => d.ModId).Concat(manifest.LoadAfter)) {
                if (direct.ContainsKey(id)) {
                    direct[mod.Id].Add(id);
                }
            }
            foreach (var id in manifest.LoadBefore.Where(direct.ContainsKey)) {
                direct[id].Add(mod.Id);
            }
        }

        var closure = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var id in direct.Keys) {
            var reached = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>(direct[id]);
            while (stack.Count > 0) {
                var next = stack.Pop();
                if (reached.Add(next)) {
                    foreach (var further in direct[next]) {
                        stack.Push(further);
                    }
                }
            }
            closure[id] = reached;
        }
        return closure;
    }

}
