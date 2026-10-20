using SpiralLoader.Api.Manifest;
using SpiralLoader.Api.Versioning;

namespace SpiralLoader.Runtime.Resolution;

/// <summary>
/// Checks dependency constraints and computes a deterministic load order.
/// </summary>
/// <remarks>
/// <para>
/// Constraint checking repeats until nothing changes, so rejections cascade: a mod whose required dependency
/// was rejected is rejected too. Mods are examined in ordinal id order, which makes the outcome independent
/// of filesystem enumeration order.
/// </para>
/// <para>
/// Ordering is a topological sort. Hard edges come from <c>depends</c>; soft edges from present
/// <c>recommends</c>, <c>loadAfter</c> and <c>loadBefore</c>. Among mods that are ready at the same time the
/// lowest id goes first. A cycle of hard edges rejects every mod on it; a cycle that needs a soft edge is
/// broken by dropping the soft edges into the lowest-id mod that has no unmet hard dependency, with a warning.
/// </para>
/// </remarks>
internal static class DependencyResolver {

    /// <summary>
    /// Validates <paramref name="candidates"/> (all in <see cref="ModState.Discovered"/> with a manifest) and
    /// returns the surviving mods in load order, each set to <see cref="ModState.Resolved"/>.
    /// </summary>
    public static IReadOnlyList<ModRecord> Resolve(IReadOnlyList<ModRecord> candidates,
                                                   IReadOnlyDictionary<string, BuiltInMod> builtIns) {
        var active = candidates
            .Where(record => record.State == ModState.Discovered && record.Manifest is not null)
            .ToDictionary(record => record.Id, StringComparer.Ordinal);

        var known = candidates
            .Where(record => record.Manifest is not null)
            .GroupBy(record => record.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        CheckConstraints(active, known, builtIns);
        return Order(active);
    }

    private static void CheckConstraints(Dictionary<string, ModRecord> active, Dictionary<string, ModRecord> known,
                                         IReadOnlyDictionary<string, BuiltInMod> builtIns) {
        // Warnings for optional dependencies are recorded once, after the fixpoint, against the final set.
        bool changed;
        do {
            changed = false;
            foreach (var record in active.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToList()) {
                var manifest = record.Manifest!;
                var problem = FindRequiredDependencyProblem(manifest, active, known, builtIns)
                              ?? FindBreaksProblem(manifest, active, builtIns);
                if (problem is not null) {
                    record.Reject(problem.Value.Code, problem.Value.Message);
                    active.Remove(record.Id);
                    changed = true;
                }
            }
        } while (changed);

        foreach (var record in active.Values) {
            foreach (var recommendation in record.Manifest!.Recommends) {
                var version = VersionOf(recommendation.ModId, active, builtIns);
                if (version is null) {
                    record.Warn(DiagnosticCodes.RecommendationMissing,
                        $"Recommended mod '{recommendation.ModId}' ({recommendation.Range}) is not installed.");
                } else if (!recommendation.Range.IsSatisfiedBy(version)) {
                    record.Warn(DiagnosticCodes.RecommendationVersionMismatch,
                        $"Recommended mod '{recommendation.ModId}' is {version}, outside {recommendation.Range}.");
                }
            }
        }
    }

    private static (string Code, string Message)? FindRequiredDependencyProblem(
        ModManifest manifest, Dictionary<string, ModRecord> active, Dictionary<string, ModRecord> known,
        IReadOnlyDictionary<string, BuiltInMod> builtIns) {
        foreach (var dependency in manifest.Depends) {
            var version = VersionOf(dependency.ModId, active, builtIns);
            if (version is null && known.TryGetValue(dependency.ModId, out var unavailable)) {
                return (DiagnosticCodes.DependencyRejected,
                    $"Requires '{dependency.ModId}' ({dependency.Range}), which is installed but {unavailable.State.ToString().ToLowerInvariant()}.");
            }
            if (version is null) {
                return (DiagnosticCodes.DependencyMissing,
                    $"Requires '{dependency.ModId}' ({dependency.Range}), which is not installed. "
                    + $"The host provides these built-ins: {string.Join(", ", builtIns.Values.OrderBy(b => b.Id, StringComparer.Ordinal).Select(b => $"{b.Id} {b.Version}"))}.");
            }
            if (!dependency.Range.IsSatisfiedBy(version)) {
                var source = active.TryGetValue(dependency.ModId, out var installed)
                    ? $"installed at {installed.Directory}"
                    : "provided by the host";
                return (DiagnosticCodes.DependencyVersionMismatch,
                    $"Requires '{dependency.ModId}' {dependency.Range}, but {version} is present ({source}).");
            }
        }
        return null;
    }

    private static (string Code, string Message)? FindBreaksProblem(
        ModManifest manifest, Dictionary<string, ModRecord> active, IReadOnlyDictionary<string, BuiltInMod> builtIns) {
        foreach (var incompatible in manifest.Breaks) {
            var version = VersionOf(incompatible.ModId, active, builtIns);
            if (version is not null && incompatible.Range.IsSatisfiedBy(version)) {
                return (DiagnosticCodes.BreaksPresent,
                    $"Is incompatible with '{incompatible.ModId}' {incompatible.Range}, and {version} is present.");
            }
        }
        return null;
    }

    private static SemanticVersion? VersionOf(string id, Dictionary<string, ModRecord> active, IReadOnlyDictionary<string, BuiltInMod> builtIns)
        => active.TryGetValue(id, out var record) ? record.Manifest!.Version
            : builtIns.TryGetValue(id, out var builtIn) ? builtIn.Version
            : null;

    private static List<ModRecord> Order(Dictionary<string, ModRecord> active) {
        var hard = active.Keys.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var soft = active.Keys.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

        // hard[x] / soft[x] hold the ids that must initialize before x.
        foreach (var record in active.Values) {
            var manifest = record.Manifest!;
            foreach (var dependency in manifest.Depends.Where(d => active.ContainsKey(d.ModId))) {
                hard[record.Id].Add(dependency.ModId);
            }
            foreach (var id in manifest.Recommends.Select(d => d.ModId).Concat(manifest.LoadAfter).Where(active.ContainsKey)) {
                soft[record.Id].Add(id);
            }
            foreach (var id in manifest.LoadBefore.Where(active.ContainsKey)) {
                soft[id].Add(record.Id);
            }
        }
        foreach (var id in active.Keys) {
            soft[id].ExceptWith(hard[id]);
        }

        var ordered = new List<ModRecord>();
        var remaining = new SortedSet<string>(active.Keys, StringComparer.Ordinal);
        while (remaining.Count > 0) {
            var next = remaining.FirstOrDefault(id => hard[id].Count == 0 && soft[id].Count == 0);
            if (next is null) {
                next = remaining.FirstOrDefault(id => hard[id].Count == 0);
                if (next is null) {
                    RejectHardCycles(remaining, hard, active);
                    break;
                }

                active[next].Warn(DiagnosticCodes.OrderingSoftCycle,
                    $"Soft ordering cycle: ignored load-after constraints on {string.Join(", ", soft[next].Order(StringComparer.Ordinal))}.");
                soft[next].Clear();
            }

            remaining.Remove(next);
            ordered.Add(active[next]);
            foreach (var id in remaining) {
                hard[id].Remove(next);
                soft[id].Remove(next);
            }
        }

        foreach (var record in ordered) {
            record.State = ModState.Resolved;
        }
        return ordered;
    }

    private static void RejectHardCycles(SortedSet<string> remaining, Dictionary<string, HashSet<string>> hard,
                                         Dictionary<string, ModRecord> active) {
        // Every remaining mod either sits on a hard cycle or (transitively) depends on one.
        var onCycle = remaining.Where(id => CanReach(id, id, hard, remaining)).ToHashSet(StringComparer.Ordinal);
        var cycleText = string.Join(" -> ", onCycle.Order(StringComparer.Ordinal));
        foreach (var id in remaining) {
            if (onCycle.Contains(id)) {
                active[id].Reject(DiagnosticCodes.DependencyCycle, $"Is part of a dependency cycle: {cycleText}.");
            } else {
                active[id].Reject(DiagnosticCodes.DependencyRejected,
                    $"Depends on mods in a dependency cycle: {cycleText}.");
            }
        }
    }

    private static bool CanReach(string from, string target, Dictionary<string, HashSet<string>> edges, SortedSet<string> scope) {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>(edges[from].Where(scope.Contains));
        while (stack.Count > 0) {
            var current = stack.Pop();
            if (current == target) {
                return true;
            }
            if (!visited.Add(current)) {
                continue;
            }
            foreach (var next in edges[current].Where(scope.Contains)) {
                stack.Push(next);
            }
        }
        return false;
    }

}
