using SpiralLoader.Api.Registries;

namespace SpiralLoader.Api.Commands;

/// <summary>
/// A text command contributed by a mod. Hosts route commands to it by <see cref="Name"/>; the Imlight
/// integration is planned to expose them as in-game chat commands (see docs/next-milestone.md), and the
/// headless host runs them from the command line.
/// </summary>
/// <param name="Name">The command word, lowercase <c>[a-z0-9_-]</c>.</param>
/// <param name="Description">One line of help text.</param>
/// <param name="Execute">Runs the command and returns the reply shown to the caller (or null for none).</param>
public sealed record ModCommand(string Name, string Description, Func<ModCommandInvocation, string?> Execute) {

    /// <summary>
    /// When true (the default) only server operators may run the command. Imlight maps operators to accounts
    /// with <c>AuthLevel.Administrator</c>; the headless host treats its console as an operator.
    /// </summary>
    public bool OperatorOnly { get; init; } = true;

}

/// <summary>A single invocation of a <see cref="ModCommand"/>.</summary>
/// <param name="Arguments">Whitespace-separated arguments after the command word.</param>
/// <param name="Caller">A host-defined description of the caller (for example, an account name).</param>
/// <param name="CallerIsOperator">True if the caller has operator rights on this host.</param>
public sealed record ModCommandInvocation(IReadOnlyList<string> Arguments, string Caller, bool CallerIsOperator = false);

/// <summary>Registries every SpiralLoader host is expected to expose.</summary>
public static class CommonRegistries {

    /// <summary>Text commands. Entry names are ignored for routing; <see cref="ModCommand.Name"/> is used.</summary>
    public static RegistryKey<ModCommand> Commands { get; } = new("spiralloader:commands");

}
