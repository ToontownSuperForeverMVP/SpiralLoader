using SpiralLoader.Api;
using SpiralLoader.Api.Commands;
using SpiralLoader.Api.Logging;

namespace HelloSpiral.Addon;

/// <summary>
/// Depends on <c>hello-spiral</c>: calls its <see cref="Greeter"/> directly and decorates its
/// <see cref="GreetingEvent"/>. Both only work because the loader shares hello-spiral's assembly with
/// this mod instead of loading a second copy.
/// </summary>
public sealed class AddonMod : ISpiralMod {

    public void Initialize(IModContext context) {
        context.Events.Subscribe<GreetingEvent>(e => e.Text += " (addon was here)");
        context.Registries.Get(CommonRegistries.Commands).Register("addon", new ModCommand(
            "addon",
            "Greets through hello-spiral's public API.",
            invocation => Greeter.Greet("addon user")));
        context.Logger.Info($"Addon initialized after {string.Join(", ", context.Mods.LoadOrder)}.");
    }

}
