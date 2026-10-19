using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace SpiralLoader.Api.Versioning;

/// <summary>
/// A version range in the npm/Fabric style used by mod manifests.
/// </summary>
/// <remarks>
/// Supported syntax:
/// <list type="bullet">
/// <item><c>*</c>, <c>x</c> or an empty string: any version.</item>
/// <item><c>1.2.3</c> or <c>=1.2.3</c>: exactly that version.</item>
/// <item><c>&gt;</c>, <c>&gt;=</c>, <c>&lt;</c>, <c>&lt;=</c> followed by a full or partial version.</item>
/// <item><c>^1.2.3</c>: compatible with 1.2.3 (same major; same minor while major is 0).</item>
/// <item><c>~1.2.3</c>: same major and minor.</item>
/// <item><c>1.2.x</c>, <c>1.2</c>, <c>1.x</c>, <c>1</c>: wildcard partial versions.</item>
/// <item>Space-separated comparators must all match; <c>||</c> separates alternatives.</item>
/// </list>
/// Exclusive upper bounds created by <c>^</c>, <c>~</c> and wildcards exclude prereleases of the bound
/// (<c>^1.0.0</c> does not match <c>2.0.0-beta</c>).
/// </remarks>
public sealed class VersionRange {

    private readonly IReadOnlyList<IReadOnlyList<Comparator>> _alternatives;

    private VersionRange(string text, IReadOnlyList<IReadOnlyList<Comparator>> alternatives) {
        Text = text;
        _alternatives = alternatives;
    }

    /// <summary>A range matching every version.</summary>
    public static VersionRange Any { get; } = new("*", [[]]);

    /// <summary>The original range text.</summary>
    public string Text { get; }

    public static VersionRange Parse(string text)
        => TryParse(text, out var range, out var error)
            ? range
            : throw new FormatException(error);

    public static bool TryParse(string? text, [NotNullWhen(true)] out VersionRange? range)
        => TryParse(text, out range, out _);

    public static bool TryParse(string? text, [NotNullWhen(true)] out VersionRange? range, out string error) {
        range = null;
        var source = text?.Trim() ?? string.Empty;
        var alternatives = new List<IReadOnlyList<Comparator>>();
        foreach (var alternative in source.Split("||")) {
            var comparators = new List<Comparator>();
            var tokens = alternative.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0 && source.Contains("||", StringComparison.Ordinal)) {
                error = $"Version range '{text}' has an empty alternative.";
                return false;
            }

            foreach (var token in tokens) {
                if (!TryParseComparator(token, comparators, out error)) {
                    error = $"Invalid version range '{text}': {error}";
                    return false;
                }
            }
            alternatives.Add(comparators);
        }

        range = new VersionRange(source.Length == 0 ? "*" : source, alternatives);
        error = string.Empty;
        return true;
    }

    /// <summary>True if <paramref name="version"/> is inside this range.</summary>
    public bool IsSatisfiedBy(SemanticVersion version) {
        ArgumentNullException.ThrowIfNull(version);
        return _alternatives.Any(set => set.All(comparator => comparator.Matches(version)));
    }

    public override string ToString() => Text;

    private static bool TryParseComparator(string token, List<Comparator> output, out string error) {
        error = string.Empty;
        string op;
        if (token.StartsWith(">=", StringComparison.Ordinal) || token.StartsWith("<=", StringComparison.Ordinal)) {
            op = token[..2];
        } else if (token[0] is '>' or '<' or '=' or '^' or '~') {
            op = token[..1];
        } else {
            op = string.Empty;
        }

        var versionText = token[op.Length..];
        if (versionText.Length == 0) {
            error = $"'{token}' is missing a version.";
            return false;
        }

        if (!TryParsePartial(versionText, out var partial, out error)) {
            return false;
        }

        // Wildcard with no operator (or '=') matches everything.
        if (partial.Major is null) {
            if (op is "" or "=" or ">=" or "<=" or "^" or "~") {
                return true;
            }
            error = $"'{token}' can never match.";
            return false;
        }

        var lower = partial.Floor();
        switch (op) {
            case "" or "=":
                if (partial.IsFull) {
                    output.Add(new Comparator(ComparatorKind.Equal, partial.Full!));
                } else {
                    output.Add(new Comparator(ComparatorKind.GreaterOrEqual, lower));
                    output.Add(new Comparator(ComparatorKind.Less, partial.NextWildcardBound()));
                }
                return true;
            case ">=":
                output.Add(new Comparator(ComparatorKind.GreaterOrEqual, partial.IsFull ? partial.Full! : lower));
                return true;
            case ">":
                if (partial.IsFull) {
                    output.Add(new Comparator(ComparatorKind.Greater, partial.Full!));
                } else {
                    var bound = partial.NextWildcardBound();
                    output.Add(new Comparator(ComparatorKind.GreaterOrEqual, new SemanticVersion(bound.Major, bound.Minor, bound.Patch)));
                }
                return true;
            case "<":
                output.Add(new Comparator(ComparatorKind.Less, partial.IsFull ? partial.Full! : WithExclusionMarker(lower)));
                return true;
            case "<=":
                if (partial.IsFull) {
                    output.Add(new Comparator(ComparatorKind.LessOrEqual, partial.Full!));
                } else {
                    output.Add(new Comparator(ComparatorKind.Less, partial.NextWildcardBound()));
                }
                return true;
            case "~": {
                    output.Add(new Comparator(ComparatorKind.GreaterOrEqual, partial.IsFull ? partial.Full! : lower));
                    var upper = partial.Minor is null
                        ? Bound(partial.Major.Value + 1, 0, 0)
                        : Bound(partial.Major.Value, partial.Minor.Value + 1, 0);
                    output.Add(new Comparator(ComparatorKind.Less, upper));
                    return true;
                }
            case "^": {
                    output.Add(new Comparator(ComparatorKind.GreaterOrEqual, partial.IsFull ? partial.Full! : lower));
                    SemanticVersion upper;
                    if (partial.Major.Value > 0 || partial.Minor is null) {
                        upper = Bound(partial.Major.Value + 1, 0, 0);
                    } else if (partial.Minor.Value > 0 || partial.Patch is null) {
                        upper = Bound(0, partial.Minor.Value + 1, 0);
                    } else {
                        upper = Bound(0, 0, partial.Patch.Value + 1);
                    }
                    output.Add(new Comparator(ComparatorKind.Less, upper));
                    return true;
                }
            default:
                error = $"Unknown operator in '{token}'.";
                return false;
        }
    }

    // "-0" is the lowest possible prerelease, so "< X.Y.Z-0" excludes X.Y.Z and all of its prereleases.
    private static SemanticVersion Bound(int major, int minor, int patch) => new(major, minor, patch, "0");

    private static SemanticVersion WithExclusionMarker(SemanticVersion version)
        => new(version.Major, version.Minor, version.Patch, "0");

    private static bool TryParsePartial(string text, out PartialVersion partial, out string error) {
        partial = default;
        error = string.Empty;
        if (text is "*" or "x" or "X") {
            partial = new PartialVersion(null, null, null, null);
            return true;
        }

        // Full versions (with optional prerelease) go through the strict parser.
        if (SemanticVersion.TryParse(text, out var full)) {
            partial = new PartialVersion(full.Major, full.Minor, full.Patch, full);
            return true;
        }

        var parts = text.Split('.');
        if (parts.Length > 3) {
            error = $"'{text}' is not a version.";
            return false;
        }

        var numbers = new int?[3];
        var sawWildcard = false;
        for (var i = 0; i < parts.Length; i++) {
            if (parts[i] is "x" or "X" or "*") {
                sawWildcard = true;
                continue;
            }
            if (sawWildcard || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value)) {
                error = $"'{text}' is not a version.";
                return false;
            }
            numbers[i] = value;
        }

        partial = new PartialVersion(numbers[0], numbers[1], numbers[2], null);
        return true;
    }

    private readonly record struct PartialVersion(int? Major, int? Minor, int? Patch, SemanticVersion? Full) {

        public bool IsFull => Full is not null;

        public SemanticVersion Floor() => new(Major ?? 0, Minor ?? 0, Patch ?? 0);

        public SemanticVersion NextWildcardBound()
            => Minor is null
                ? Bound(Major!.Value + 1, 0, 0)
                : Patch is null
                    ? Bound(Major!.Value, Minor.Value + 1, 0)
                    : Bound(Major!.Value, Minor.Value, Patch.Value + 1);

    }

    private enum ComparatorKind {
        Equal,
        Greater,
        GreaterOrEqual,
        Less,
        LessOrEqual,
    }

    private sealed record Comparator(ComparatorKind Kind, SemanticVersion Version) {

        public bool Matches(SemanticVersion candidate) {
            var comparison = candidate.CompareTo(Version);
            return Kind switch {
                ComparatorKind.Equal => comparison == 0,
                ComparatorKind.Greater => comparison > 0,
                ComparatorKind.GreaterOrEqual => comparison >= 0,
                ComparatorKind.Less => comparison < 0,
                ComparatorKind.LessOrEqual => comparison <= 0,
                _ => false,
            };
        }

    }

}
