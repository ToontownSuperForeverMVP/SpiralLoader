using System.Text.RegularExpressions;
using SpiralLoader.Api.Commands;
using SpiralLoader.Api.Logging;
using SpiralLoader.Api.Registries;

namespace SpiralLoader.Runtime;

/// <summary>
/// Routes text commands to <see cref="ModCommand"/> registrations. When two mods register the same command
/// name, the one earlier in load order wins and the duplicate is logged.
/// </summary>
public sealed partial class ModCommandRouter {

    private readonly Dictionary<string, RegistryEntry<ModCommand>> _commands = new(StringComparer.OrdinalIgnoreCase);

    public ModCommandRouter(IEnumerable<RegistryEntry<ModCommand>> entries, IModLogger logger) {
        foreach (var entry in entries) {
            if (!_commands.TryAdd(entry.Value.Name, entry)) {
                logger.Warn($"Command '{entry.Value.Name}' from '{entry.OwnerModId}' is ignored: '{_commands[entry.Value.Name].OwnerModId}' registered it first.");
            }
        }
    }

    /// <summary>Validator for <see cref="CommonRegistries.Commands"/>.</summary>
    public static string? Validate(ModCommand command)
        => CommandNamePattern().IsMatch(command.Name) ? null : $"command name '{command.Name}' must be lowercase [a-z0-9_-].";

    public IReadOnlyCollection<RegistryEntry<ModCommand>> Commands => _commands.Values;

    /// <summary>Runs <paramref name="commandLine"/>. Returns false if no mod registered its command word.</summary>
    public bool TryExecute(string commandLine, string caller, out string? reply) {
        reply = null;
        var parts = commandLine.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !_commands.TryGetValue(parts[0], out var entry)) {
            return false;
        }
        reply = entry.Value.Execute(new ModCommandInvocation(parts[1..], caller));
        return true;
    }

    [GeneratedRegex("^[a-z0-9_-]{1,32}$")]
    private static partial Regex CommandNamePattern();

}
