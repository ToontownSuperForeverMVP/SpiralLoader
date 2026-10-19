using SpiralLoader.Api.Commands;
using SpiralLoader.Api.Events;
using SpiralLoader.Api.Logging;
using SpiralLoader.Api.Registries;
using SpiralLoader.Runtime;
using SpiralLoader.Runtime.Events;
using SpiralLoader.Runtime.Logging;
using SpiralLoader.Runtime.Registries;

namespace SpiralLoader.Tests;

public sealed class EventAndRegistryTests {

    private sealed class Sample {
        public List<string> Calls { get; } = [];
    }

    private sealed class Cancellable : CancellableEvent {
        public List<string> Calls { get; } = [];
    }

    private static (EventBus Bus, MemoryLogSink Log) CreateBus(HandlerFaultPolicy policy = HandlerFaultPolicy.Isolate) {
        var log = new MemoryLogSink();
        return (new EventBus(new SinkLogger("spiralloader", log, ModLogLevel.Trace), policy), log);
    }

    [Fact]
    public void DispatchesByPriorityThenLoadOrderThenRegistration() {
        var (bus, _) = CreateBus();
        bus.Subscribe<Sample>("mod-b", 1, e => e.Calls.Add("b-normal"), EventPriority.Normal);
        bus.Subscribe<Sample>("mod-a", 0, e => e.Calls.Add("a-normal-1"), EventPriority.Normal);
        bus.Subscribe<Sample>("mod-b", 1, e => e.Calls.Add("b-high"), EventPriority.High);
        bus.Subscribe<Sample>("mod-a", 0, e => e.Calls.Add("a-normal-2"), EventPriority.Normal);
        bus.Subscribe<Sample>("host", -1, e => e.Calls.Add("host-low"), EventPriority.Low);

        var sample = new Sample();
        bus.Publish(sample);

        Assert.Equal(["b-high", "a-normal-1", "a-normal-2", "b-normal", "host-low"], sample.Calls);
    }

    [Fact]
    public void DispatchIsByExactType() {
        var (bus, _) = CreateBus();
        var calls = 0;
        bus.Subscribe<object>("mod", 0, _ => calls++, 0);
        bus.Publish(new Sample());
        Assert.Equal(0, calls);
    }

    [Fact]
    public void CancellationSkipsLaterHandlersExceptMonitors() {
        var (bus, _) = CreateBus();
        bus.Subscribe<Cancellable>("first", 0, e => { e.Calls.Add("first"); e.Cancel(); }, EventPriority.High);
        bus.Subscribe<Cancellable>("second", 1, e => e.Calls.Add("second"), EventPriority.Normal);
        bus.Subscribe<Cancellable>("monitor", 2, e => e.Calls.Add($"monitor:{e.IsCancelled}"), EventPriority.Monitor);

        var e = new Cancellable();
        bus.Publish(e);

        Assert.Equal(["first", "monitor:True"], e.Calls);
    }

    [Fact]
    public void IsolatesHandlerExceptionsAndCountsFaults() {
        var (bus, log) = CreateBus();
        bus.Subscribe<Sample>("broken", 0, _ => throw new InvalidOperationException("kaboom"), 10);
        bus.Subscribe<Sample>("healthy", 1, e => e.Calls.Add("healthy"), 0);

        var sample = new Sample();
        bus.Publish(sample);
        bus.Publish(sample);

        Assert.Equal(["healthy", "healthy"], sample.Calls);
        Assert.Equal(2, bus.FaultCounts["broken"]);
        Assert.Contains(log.Entries, entry => entry.Level == ModLogLevel.Error && entry.Message.Contains("'broken'") && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public void RethrowPolicyIdentifiesTheOwner() {
        var (bus, _) = CreateBus(HandlerFaultPolicy.Rethrow);
        bus.Subscribe<Sample>("broken", 0, _ => throw new InvalidOperationException("kaboom"), 0);

        var exception = Assert.Throws<ModEventHandlerException>(() => bus.Publish(new Sample()));

        Assert.Equal("broken", exception.Owner);
        Assert.Equal(typeof(Sample), exception.EventType);
    }

    [Fact]
    public void FreezeClosesSubscriptions() {
        var (bus, _) = CreateBus();
        bus.Subscribe<Sample>("early", 0, _ => { }, 0);
        bus.Freeze();

        Assert.Throws<RegistrationClosedException>(() => bus.Subscribe<Sample>("late", 1, _ => { }, 0));
        Assert.True(bus.HasSubscribers<Sample>());
    }

    [Fact]
    public void RemoveOwnerDropsOnlyThatOwner() {
        var (bus, _) = CreateBus();
        bus.Subscribe<Sample>("keep", 0, e => e.Calls.Add("keep"), 0);
        bus.Subscribe<Sample>("drop", 1, e => e.Calls.Add("drop"), 0);
        bus.Subscribe<Cancellable>("drop", 1, _ => { }, 0);

        Assert.Equal(2, bus.RemoveOwner("drop"));
        var sample = new Sample();
        bus.Publish(sample);
        Assert.Equal(["keep"], sample.Calls);
        Assert.False(bus.HasSubscribers<Cancellable>());
    }

    [Fact]
    public async Task ConcurrentPublishingAfterFreezeIsSafe() {
        var (bus, _) = CreateBus();
        var total = 0;
        bus.Subscribe<Sample>("counter", 0, _ => Interlocked.Increment(ref total), 0);
        bus.Freeze();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => {
            for (var i = 0; i < 10_000; i++) {
                bus.Publish(new Sample());
            }
        })));

        Assert.Equal(80_000, total);
    }

    [Fact]
    public void NullEventBusIsInert() {
        Assert.False(NullEventBus.Instance.HasSubscribers<Sample>());
        NullEventBus.Instance.Publish(new Sample());
        Assert.Throws<RegistrationClosedException>(() => NullEventBus.Instance.Subscribe<Sample>(_ => { }));
    }

    [Fact]
    public void RegistryNamespacesEntriesAndKeepsOrder() {
        var store = new RegistryStore();
        var key = new RegistryKey<string>("test:things");
        store.Add(key, value => value.Length == 0 ? "empty values are not allowed" : null);
        var first = new ScopedRegistryProvider(store, "first-mod").Get(key);
        var second = new ScopedRegistryProvider(store, "second-mod").Get(key);

        first.Register("alpha", "A");
        second.Register("alpha", "B");
        first.Register("beta", "C");

        Assert.Equal(["first-mod:alpha", "second-mod:alpha", "first-mod:beta"], first.Entries.Select(e => e.Id));
        Assert.Throws<ArgumentException>(() => first.Register("alpha", "again"));
        Assert.Throws<ArgumentException>(() => first.Register("Bad Name", "x"));
        var rejected = Assert.Throws<ArgumentException>(() => first.Register("gamma", ""));
        Assert.Contains("empty values are not allowed", rejected.Message);

        Assert.Equal(2, store.RemoveOwner("first-mod"));
        Assert.Equal(["second-mod:alpha"], second.Entries.Select(e => e.Id));

        store.Freeze();
        Assert.Throws<RegistrationClosedException>(() => second.Register("late", "x"));
    }

    [Fact]
    public void UnknownRegistryIsReported() {
        var provider = new ScopedRegistryProvider(new RegistryStore(), "mod");
        var key = new RegistryKey<string>("test:missing");
        Assert.False(provider.TryGet(key, out _));
        Assert.Throws<KeyNotFoundException>(() => provider.Get(key));
        Assert.Throws<ArgumentException>(() => new RegistryKey<string>("no-namespace"));
    }

    [Fact]
    public void CommandRouterPrefersEarlierModAndValidatesNames() {
        var log = new MemoryLogSink();
        RegistryEntry<ModCommand> Entry(string owner, string reply) => new($"{owner}:greet", owner, new ModCommand("greet", "", i => reply + string.Join(' ', i.Arguments)));
        var router = new ModCommandRouter([Entry("early", "early "), Entry("late", "late ")], new SinkLogger("spiralloader", log, ModLogLevel.Trace));

        Assert.True(router.TryExecute("GREET a b", "tester", out var reply));
        Assert.Equal("early a b", reply);
        Assert.False(router.TryExecute("unknown", "tester", out _));
        Assert.Contains(log.Entries, e => e.Message.Contains("'late'"));
        Assert.NotNull(ModCommandRouter.Validate(new ModCommand("Has Space", "", _ => null)));
        Assert.Null(ModCommandRouter.Validate(new ModCommand("ok-name", "", _ => null)));
    }

}
