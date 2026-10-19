using SpiralLoader.Api.Events;
using SpiralLoader.Api.Registries;

namespace SpiralLoader.Runtime.Registries;

/// <summary>
/// Validates a value contributed to a registry. Return null to accept, or a reason to reject.
/// </summary>
public delegate string? RegistryValidator<in T>(T value);

internal interface IOwnedRegistry {

    string Name { get; }

    int RemoveOwner(string owner);

    void Freeze();

}

/// <summary>A registry shared by every mod. Mods see it through <see cref="ScopedRegistry{T}"/>.</summary>
internal sealed class Registry<T>(RegistryKey<T> key, RegistryValidator<T>? validator) : IOwnedRegistry where T : notnull {

    private readonly Lock _gate = new();
    private RegistryEntry<T>[] _entries = [];
    private bool _frozen;

    public RegistryKey<T> Key { get; } = key;

    public string Name => Key.Name;

    public IReadOnlyList<RegistryEntry<T>> Entries => Volatile.Read(ref _entries);

    public RegistryEntry<T> Register(string owner, string name, T value) {
        ArgumentNullException.ThrowIfNull(value);
        if (!RegistryNames.IsValidPath(name)) {
            throw new ArgumentException($"Registry entry name '{name}' must be lowercase [a-z0-9_./-].", nameof(name));
        }

        var rejection = validator?.Invoke(value);
        if (rejection is not null) {
            throw new ArgumentException($"{Key.Name} rejected '{owner}:{name}': {rejection}", nameof(value));
        }

        lock (_gate) {
            if (_frozen) {
                throw new RegistrationClosedException(
                    $"'{owner}' tried to register in {Key.Name} after registrations closed. Register during Initialize.");
            }

            var id = $"{owner}:{name}";
            if (_entries.Any(entry => entry.Id == id)) {
                throw new ArgumentException($"{Key.Name} already contains '{id}'.", nameof(name));
            }

            var entry = new RegistryEntry<T>(id, owner, value);
            Volatile.Write(ref _entries, [.. _entries, entry]);
            return entry;
        }
    }

    public int RemoveOwner(string owner) {
        lock (_gate) {
            var kept = _entries.Where(entry => entry.OwnerModId != owner).ToArray();
            var removed = _entries.Length - kept.Length;
            Volatile.Write(ref _entries, kept);
            return removed;
        }
    }

    public void Freeze() {
        lock (_gate) {
            _frozen = true;
        }
    }

}

/// <summary>Owns every registry the host declared.</summary>
internal sealed class RegistryStore {

    private readonly Dictionary<string, IOwnedRegistry> _registries = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public void Add<T>(RegistryKey<T> key, RegistryValidator<T>? validator) where T : notnull {
        lock (_gate) {
            if (!_registries.TryAdd(key.Name, new Registry<T>(key, validator))) {
                throw new InvalidOperationException($"Registry {key.Name} is already declared.");
            }
        }
    }

    public Registry<T>? Find<T>(RegistryKey<T> key) where T : notnull {
        lock (_gate) {
            if (!_registries.TryGetValue(key.Name, out var registry)) {
                return null;
            }
            return registry as Registry<T>
                ?? throw new InvalidOperationException($"Registry {key.Name} holds a different value type than {typeof(T).Name}.");
        }
    }

    public int RemoveOwner(string owner) {
        lock (_gate) {
            return _registries.Values.Sum(registry => registry.RemoveOwner(owner));
        }
    }

    public void Freeze() {
        lock (_gate) {
            foreach (var registry in _registries.Values) {
                registry.Freeze();
            }
        }
    }

}

/// <summary>A mod's view of a registry: its registrations are owned by, and namespaced with, the mod id.</summary>
internal sealed class ScopedRegistry<T>(Registry<T> registry, string owner) : IRegistry<T> where T : notnull {

    public RegistryKey<T> Key => registry.Key;

    public IReadOnlyList<RegistryEntry<T>> Entries => registry.Entries;

    public RegistryEntry<T> Register(string name, T value) => registry.Register(owner, name, value);

}

internal sealed class ScopedRegistryProvider(RegistryStore store, string owner) : IRegistryProvider {

    public IRegistry<T> Get<T>(RegistryKey<T> key) where T : notnull
        => TryGet(key, out var registry)
            ? registry!
            : throw new KeyNotFoundException($"This host does not expose registry {key.Name}.");

    public bool TryGet<T>(RegistryKey<T> key, out IRegistry<T>? registry) where T : notnull {
        var found = store.Find(key);
        registry = found is null ? null : new ScopedRegistry<T>(found, owner);
        return found is not null;
    }

}
