using System.Diagnostics;
using SpiralLoader.Api.Commands;
using SpiralLoader.Api.Events;
using SpiralLoader.Runtime;
using SpiralLoader.Tests.Infrastructure;

namespace SpiralLoader.Tests;

/// <summary>
/// The acceptance path for this milestone: the example mods are separate projects, built independently and
/// assembled into <c>artifacts/mods</c>; nothing in this test assembly references them at compile time.
/// </summary>
public sealed class ExampleModIntegrationTests {

    [Fact]
    public void ExampleModsAreBuiltAndNotReferencedByTests() {
        Assert.True(File.Exists(Path.Combine(BuildPaths.ExampleModsDirectory, "hello-spiral", "HelloSpiral.dll")));
        Assert.True(File.Exists(Path.Combine(BuildPaths.ExampleModsDirectory, "hello-spiral-addon", "HelloSpiral.Addon.dll")));
        Assert.DoesNotContain(typeof(ExampleModIntegrationTests).Assembly.GetReferencedAssemblies(), a => a.Name!.StartsWith("HelloSpiral", StringComparison.Ordinal));
        // The addon must not ship hello-spiral or the API; both come from elsewhere at runtime.
        Assert.False(File.Exists(Path.Combine(BuildPaths.ExampleModsDirectory, "hello-spiral-addon", "HelloSpiral.dll")));
        Assert.False(File.Exists(Path.Combine(BuildPaths.ExampleModsDirectory, "hello-spiral-addon", "SpiralLoader.Api.dll")));
    }

    [Fact]
    public void LoadsExampleModsInProcess() {
        using var workspace = new ModWorkspace();
        var host = workspace.CreateHost(o => {
            o.ModDirectories.Clear();
            o.ModDirectories.Add(BuildPaths.ExampleModsDirectory);
            o.FailurePolicy = ModFailurePolicy.AbortStartup;
        });
        host.AddRegistry(CommonRegistries.Commands, ModCommandRouter.Validate);

        var report = host.Load();
        host.Events.Publish(new ServerStartedEvent("TestHost"));

        Assert.Equal(["hello-spiral", "hello-spiral-addon"], report.LoadOrder);
        var router = new ModCommandRouter(host.Registries.Get(CommonRegistries.Commands).Entries, host.Logger);
        Assert.True(router.TryExecute("hello Merle", "tester", callerIsOperator: true, out var reply));
        Assert.Equal("Hello, Merle! Welcome to the Spiral. (addon was here)", reply);
        Assert.True(File.Exists(Path.Combine(workspace.DataRoot, "default", "hello-spiral", "started.txt")));

        var plan = host.PlanAssets();
        Assert.Equal("Scripts/HelloSpiral/Hello.lua", Assert.Single(plan.Overrides).EntryPath);
        host.Shutdown();
    }

    [Fact]
    public async Task HeadlessHostProcessLoadsExampleMods() {
        using var workDirectory = new TempDirectory();
        var result = await RunHost(workDirectory.Path, "--mods", BuildPaths.ExampleModsDirectory, "--profile", "ci", "--run", "addon");

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("[hello-spiral] Hello from Hello Spiral 1.0.0 on SpiralLoader.Host.", result.Output);
        Assert.Contains("Hello, addon user! Welcome to the Spiral.", result.Output);
        Assert.Equal("SpiralLoader.Host ci", File.ReadAllText(Path.Combine(workDirectory.Path, "moddata", "ci", "hello-spiral", "started.txt")));
    }

    [Fact]
    public async Task HeadlessHostExitsNonZeroWhenAModFails() {
        using var workDirectory = new TempDirectory();
        var result = await RunHost(workDirectory.Path, "--mods", BuildPaths.ExampleModsDirectory, "--disable", "hello-spiral");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(DiagnosticCodes.DependencyRejected, result.Output);
    }

    private static async Task<(int ExitCode, string Output)> RunHost(string workingDirectory, params string[] arguments) {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var start = new ProcessStartInfo(dotnet) {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(BuildPaths.HeadlessHostPath);
        foreach (var argument in arguments) {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

}
