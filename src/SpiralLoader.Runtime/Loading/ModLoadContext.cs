using System.Reflection;
using System.Runtime.Loader;

namespace SpiralLoader.Runtime;

/// <summary>
/// The <see cref="AssemblyLoadContext"/> a mod's assemblies live in. One per mod, never collectible:
/// mods are loaded once per process and require a restart to change.
/// </summary>
/// <remarks>
/// Resolution order for a reference made by the mod:
/// <list type="number">
/// <item>Assemblies the host can provide (SpiralLoader.Api, Imlight, Akka, the framework) always come from
/// the default context, so every mod sees the same <c>Type</c> identities as the host, even if the mod
/// shipped its own copy.</item>
/// <item>Assemblies declared by the mod's dependencies come from those mods' contexts, so mods can share
/// types with the mods they depend on.</item>
/// <item>Everything else is private to the mod: resolved through its <c>.deps.json</c> or from its directory.</item>
/// </list>
/// </remarks>
internal sealed class ModLoadContext : AssemblyLoadContext {

    private readonly string _modId;
    private readonly string _modDirectory;
    private readonly SharedAssemblyPolicy _shared;
    private readonly IReadOnlyList<ModLoadContext> _dependencies;
    private readonly List<AssemblyDependencyResolver> _resolvers = [];
    private readonly HashSet<string> _declaredNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string>? _onSharedCopy;

    public ModLoadContext(string modId, string modDirectory, SharedAssemblyPolicy shared,
                          IReadOnlyList<ModLoadContext> dependencies, Action<string>? onSharedCopy = null)
        : base($"SpiralLoader.Mod:{modId}", isCollectible: false) {
        _modId = modId;
        _modDirectory = Path.GetFullPath(modDirectory);
        _shared = shared;
        _dependencies = dependencies;
        _onSharedCopy = onSharedCopy;
    }

    public string ModId => _modId;

    /// <summary>Loads one of the mod's declared assemblies by path.</summary>
    public Assembly LoadDeclared(string path) {
        var fullPath = Path.GetFullPath(path);
        var name = AssemblyName.GetAssemblyName(fullPath);
        if (name.Name is not null && _shared.IsShared(name.Name)) {
            throw new BadImageFormatException(
                $"'{Path.GetFileName(fullPath)}' is provided by the host ({name.Name}) and cannot be declared as a mod assembly.");
        }

        try {
            _resolvers.Add(new AssemblyDependencyResolver(fullPath));
        } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) {
            // No usable .deps.json next to the assembly; directory probing below still works.
        }

        if (name.Name is not null) {
            _declaredNames.Add(name.Name);
        }
        return LoadFromAssemblyPath(fullPath);
    }

    /// <summary>True if <paramref name="simpleName"/> is one of this mod's declared assemblies.</summary>
    public bool Declares(string simpleName) => _declaredNames.Contains(simpleName);

    protected override Assembly? Load(AssemblyName assemblyName) {
        var simpleName = assemblyName.Name;
        if (simpleName is null) {
            return null;
        }

        // 1. Host first. Returning null defers to the default context.
        if (_shared.IsShared(simpleName)) {
            if (FindPrivateCopy(assemblyName) is not null) {
                _onSharedCopy?.Invoke(simpleName);
            }
            return null;
        }

        // 2. Assemblies owned by dependencies (transitively).
        foreach (var dependency in _dependencies) {
            var shared = dependency.TryLoadForDependent(assemblyName);
            if (shared is not null) {
                return shared;
            }
        }

        // 3. Private to this mod.
        var path = FindPrivateCopy(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) {
        foreach (var resolver in _resolvers) {
            var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (path is not null) {
                return LoadUnmanagedDllFromPath(path);
            }
        }
        return IntPtr.Zero;
    }

    private Assembly? TryLoadForDependent(AssemblyName assemblyName) {
        if (assemblyName.Name is not null && Declares(assemblyName.Name)) {
            return LoadFromAssemblyName(assemblyName);
        }
        foreach (var dependency in _dependencies) {
            var shared = dependency.TryLoadForDependent(assemblyName);
            if (shared is not null) {
                return shared;
            }
        }
        return null;
    }

    private string? FindPrivateCopy(AssemblyName assemblyName) {
        foreach (var resolver in _resolvers) {
            var resolved = resolver.ResolveAssemblyToPath(assemblyName);
            if (resolved is not null && File.Exists(resolved)) {
                return resolved;
            }
        }

        var probe = Path.Combine(_modDirectory, assemblyName.Name + ".dll");
        return File.Exists(probe) ? probe : null;
    }

    public override string ToString() => Name ?? _modId;

}

/// <summary>
/// Decides which assemblies always come from the host. An assembly is shared if it is the public API, is on
/// the host's trusted platform assembly list (the host's own dependencies), or is already loaded into the
/// default context.
/// </summary>
internal sealed class SharedAssemblyPolicy {

    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);

    public SharedAssemblyPolicy(IEnumerable<string>? additionalSharedNames = null) {
        _names.Add(typeof(Api.ISpiralMod).Assembly.GetName().Name!);
        _names.Add(typeof(SharedAssemblyPolicy).Assembly.GetName().Name!);

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa) {
            foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
                _names.Add(Path.GetFileNameWithoutExtension(path));
            }
        }

        foreach (var name in additionalSharedNames ?? []) {
            _names.Add(name);
        }
    }

    public bool IsShared(string simpleName) {
        if (_names.Contains(simpleName)) {
            return true;
        }

        foreach (var assembly in AssemblyLoadContext.Default.Assemblies) {
            if (string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

}
