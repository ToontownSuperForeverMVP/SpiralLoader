using System.Text.RegularExpressions;

namespace SpiralLoader.Api.Registries;

/// <summary>
/// Identifies a registry the host exposes, for example <c>imlight:game_services</c>.
/// </summary>
/// <typeparam name="T">The type of value contributed to the registry.</typeparam>
public sealed class RegistryKey<T> : IEquatable<RegistryKey<T>> where T : notnull {

    public RegistryKey(string name) {
        if (!RegistryNames.IsValidQualifiedName(name)) {
            throw new ArgumentException($"Registry name '{name}' must look like 'namespace:path'.", nameof(name));
        }
        Name = name;
    }

    public string Name { get; }

    public bool Equals(RegistryKey<T>? other) => other is not null && other.Name == Name;

    public override bool Equals(object? obj) => obj is RegistryKey<T> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Name, typeof(T));

    public override string ToString() => $"{Name} ({typeof(T).Name})";

}

/// <summary>
/// A contribution in a registry. <see cref="Id"/> is namespaced by the contributing mod: <c>modid:name</c>.
/// </summary>
public sealed record RegistryEntry<T>(string Id, string OwnerModId, T Value) where T : notnull;

/// <summary>
/// A registry of mod contributions. Entries are ordered by mod load order, then registration order.
/// </summary>
public interface IRegistry<T> where T : notnull {

    RegistryKey<T> Key { get; }

    /// <summary>
    /// Adds a contribution owned by the calling mod. The final id is <c>modid:<paramref name="name"/></c>.
    /// </summary>
    /// <exception cref="ArgumentException">The name is invalid, already used, or the host rejected the value.</exception>
    /// <exception cref="Events.RegistrationClosedException">Registrations have closed.</exception>
    RegistryEntry<T> Register(string name, T value);

    /// <summary>All entries in deterministic order.</summary>
    IReadOnlyList<RegistryEntry<T>> Entries { get; }

}

/// <summary>Resolves registries exposed by the host.</summary>
public interface IRegistryProvider {

    /// <summary>Gets a registry, or throws <see cref="KeyNotFoundException"/> if this host does not expose it.</summary>
    IRegistry<T> Get<T>(RegistryKey<T> key) where T : notnull;

    bool TryGet<T>(RegistryKey<T> key, out IRegistry<T>? registry) where T : notnull;

}

/// <summary>A provider that exposes no registries (used when no loader is attached).</summary>
public sealed class EmptyRegistryProvider : IRegistryProvider {

    public static EmptyRegistryProvider Instance { get; } = new();

    private EmptyRegistryProvider() { }

    public IRegistry<T> Get<T>(RegistryKey<T> key) where T : notnull
        => throw new KeyNotFoundException($"Registry {key.Name} is not available: no mod loader is attached.");

    public bool TryGet<T>(RegistryKey<T> key, out IRegistry<T>? registry) where T : notnull {
        registry = null;
        return false;
    }

}

/// <summary>Validation helpers for registry and entry names.</summary>
public static partial class RegistryNames {

    /// <summary>True for <c>namespace:path</c>, both parts lowercase <c>[a-z0-9_.-/]</c>.</summary>
    public static bool IsValidQualifiedName(string? name) => name is not null && QualifiedName().IsMatch(name);

    /// <summary>True for an unqualified entry name, lowercase <c>[a-z0-9_.-/]</c>.</summary>
    public static bool IsValidPath(string? name) => name is not null && PathName().IsMatch(name);

    [GeneratedRegex("^[a-z0-9_.-]+:[a-z0-9_./-]+$")]
    private static partial Regex QualifiedName();

    [GeneratedRegex("^[a-z0-9_./-]+$")]
    private static partial Regex PathName();

}
