using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace SpiralLoader.Api.Versioning;

/// <summary>
/// A Semantic Versioning 2.0.0 version (<c>MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]</c>).
/// Build metadata is preserved but ignored for ordering and equality, per the specification.
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion> {

    private static readonly string[] s_noIdentifiers = [];

    public SemanticVersion(int major, int minor, int patch, string? prerelease = null, string? build = null) {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);
        Major = major;
        Minor = minor;
        Patch = patch;
        PrereleaseIdentifiers = string.IsNullOrEmpty(prerelease) ? s_noIdentifiers : prerelease.Split('.');
        Build = string.IsNullOrEmpty(build) ? null : build;
        foreach (var identifier in PrereleaseIdentifiers) {
            if (!IsValidIdentifier(identifier, isPrerelease: true)) {
                throw new ArgumentException($"Invalid prerelease identifier '{identifier}'.", nameof(prerelease));
            }
        }
        if (Build is not null && Build.Split('.').Any(identifier => !IsValidIdentifier(identifier, isPrerelease: false))) {
            throw new ArgumentException($"Invalid build metadata '{build}'.", nameof(build));
        }
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public IReadOnlyList<string> PrereleaseIdentifiers { get; }

    public string? Prerelease => PrereleaseIdentifiers.Count == 0 ? null : string.Join('.', PrereleaseIdentifiers);

    public string? Build { get; }

    public bool IsPrerelease => PrereleaseIdentifiers.Count > 0;

    public static SemanticVersion Parse(string text)
        => TryParse(text, out var version, out var error)
            ? version
            : throw new FormatException(error);

    public static bool TryParse(string? text, [NotNullWhen(true)] out SemanticVersion? version)
        => TryParse(text, out version, out _);

    public static bool TryParse(string? text, [NotNullWhen(true)] out SemanticVersion? version, out string error) {
        version = null;
        if (string.IsNullOrWhiteSpace(text)) {
            error = "Version is empty.";
            return false;
        }

        var remaining = text.Trim();
        string? build = null;
        var plus = remaining.IndexOf('+');
        if (plus >= 0) {
            build = remaining[(plus + 1)..];
            remaining = remaining[..plus];
            if (build.Length == 0) {
                error = $"Version '{text}' has empty build metadata.";
                return false;
            }
        }

        string? prerelease = null;
        var dash = remaining.IndexOf('-');
        if (dash >= 0) {
            prerelease = remaining[(dash + 1)..];
            remaining = remaining[..dash];
            if (prerelease.Length == 0) {
                error = $"Version '{text}' has an empty prerelease.";
                return false;
            }
        }

        var parts = remaining.Split('.');
        if (parts.Length != 3
            || !TryParseNumber(parts[0], out var major)
            || !TryParseNumber(parts[1], out var minor)
            || !TryParseNumber(parts[2], out var patch)) {
            error = $"Version '{text}' is not in MAJOR.MINOR.PATCH form.";
            return false;
        }

        if (prerelease is not null && prerelease.Split('.').Any(identifier => !IsValidIdentifier(identifier, isPrerelease: true))) {
            error = $"Version '{text}' has an invalid prerelease.";
            return false;
        }

        if (build is not null && build.Split('.').Any(identifier => !IsValidIdentifier(identifier, isPrerelease: false))) {
            error = $"Version '{text}' has invalid build metadata.";
            return false;
        }

        version = new SemanticVersion(major, minor, patch, prerelease, build);
        error = string.Empty;
        return true;
    }

    public int CompareTo(SemanticVersion? other) {
        if (other is null) {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        if (result != 0) {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0) {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0) {
            return result;
        }

        // A version without a prerelease has higher precedence than one with a prerelease.
        if (!IsPrerelease || !other.IsPrerelease) {
            return other.IsPrerelease.CompareTo(IsPrerelease);
        }

        var count = Math.Min(PrereleaseIdentifiers.Count, other.PrereleaseIdentifiers.Count);
        for (var i = 0; i < count; i++) {
            result = CompareIdentifiers(PrereleaseIdentifiers[i], other.PrereleaseIdentifiers[i]);
            if (result != 0) {
                return result;
            }
        }

        return PrereleaseIdentifiers.Count.CompareTo(other.PrereleaseIdentifiers.Count);
    }

    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    public override string ToString() {
        var text = $"{Major}.{Minor}.{Patch}";
        if (IsPrerelease) {
            text += "-" + Prerelease;
        }
        if (Build is not null) {
            text += "+" + Build;
        }
        return text;
    }

    public static bool operator ==(SemanticVersion? left, SemanticVersion? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    private static int CompareIdentifiers(string left, string right) {
        var leftNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
        var rightNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
        return (leftNumeric, rightNumeric) switch {
            (true, true) => leftNumber.CompareTo(rightNumber),
            (true, false) => -1,
            (false, true) => 1,
            _ => string.CompareOrdinal(left, right),
        };
    }

    private static bool TryParseNumber(string text, out int value) {
        value = 0;
        if (text.Length == 0 || (text.Length > 1 && text[0] == '0')) {
            return false;
        }
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsValidIdentifier(string identifier, bool isPrerelease) {
        if (identifier.Length == 0 || !identifier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) {
            return false;
        }

        // Numeric prerelease identifiers must not have leading zeroes.
        return !isPrerelease
            || !identifier.All(char.IsAsciiDigit)
            || identifier.Length == 1
            || identifier[0] != '0';
    }

}
