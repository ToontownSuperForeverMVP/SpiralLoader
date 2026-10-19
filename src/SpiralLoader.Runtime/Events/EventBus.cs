using System.Collections.Concurrent;
using SpiralLoader.Api.Events;
using SpiralLoader.Api.Logging;

namespace SpiralLoader.Runtime.Events;

/// <summary>What happens when an event handler throws.</summary>
public enum HandlerFaultPolicy {

    /// <summary>Log the exception against the owning mod and continue with the next handler. Default.</summary>
    Isolate,

    /// <summary>Wrap the exception in <see cref="ModEventHandlerException"/> and throw it to the publisher.</summary>
    Rethrow,

}

/// <summary>An event handler owned by a mod threw while <see cref="HandlerFaultPolicy.Rethrow"/> was active.</summary>
public sealed class ModEventHandlerException(string owner, Type eventType, Exception inner)
    : Exception($"Handler for {eventType.Name} owned by '{owner}' threw: {inner.Message}", inner) {

    public string Owner { get; } = owner;

    public Type EventType { get; } = eventType;

}

/// <summary>
/// The shared event bus. Mods use owner-scoped views (<see cref="ScopedEventBus"/>), so every subscription
/// knows which mod it belongs to and can be removed if that mod fails to initialize.
/// </summary>
/// <remarks>
/// Handler lists are immutable arrays replaced on write (copy-on-write), so <see cref="Publish{TEvent}"/>
/// never locks; after <see cref="Freeze"/> no writes happen at all.
/// </remarks>
internal sealed class EventBus {

    private readonly ConcurrentDictionary<Type, Subscription[]> _handlers = new();
    private readonly ConcurrentDictionary<string, int> _faults = new(StringComparer.Ordinal);
    private readonly Lock _writeGate = new();
    private readonly IModLogger _logger;
    private long _sequence;
    private volatile bool _frozen;

    public EventBus(IModLogger logger, HandlerFaultPolicy faultPolicy) {
        _logger = logger;
        FaultPolicy = faultPolicy;
    }

    public HandlerFaultPolicy FaultPolicy { get; }

    public bool IsFrozen => _frozen;

    /// <summary>Handler exceptions caught so far, by owner.</summary>
    public IReadOnlyDictionary<string, int> FaultCounts => _faults;

    public void Subscribe<TEvent>(string owner, int ownerOrder, Action<TEvent> handler, int priority) where TEvent : class {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_writeGate) {
            if (_frozen) {
                throw new RegistrationClosedException(
                    $"'{owner}' tried to subscribe to {typeof(TEvent).Name} after registrations closed. " +
                    "Subscribe during Initialize; changing subscriptions requires a restart.");
            }

            var subscription = new Subscription(owner, ownerOrder, priority, ++_sequence, handler);
            var existing = _handlers.GetValueOrDefault(typeof(TEvent)) ?? [];
            var updated = new Subscription[existing.Length + 1];
            existing.CopyTo(updated, 0);
            updated[^1] = subscription;
            Array.Sort(updated, Subscription.DispatchOrder);
            _handlers[typeof(TEvent)] = updated;
        }
    }

    public void Publish<TEvent>(TEvent @event) where TEvent : class {
        ArgumentNullException.ThrowIfNull(@event);
        if (!_handlers.TryGetValue(typeof(TEvent), out var subscriptions)) {
            return;
        }

        var cancellable = @event as CancellableEvent;
        foreach (var subscription in subscriptions) {
            if (cancellable is { IsCancelled: true } && subscription.Priority != EventPriority.Monitor) {
                continue;
            }

            try {
                ((Action<TEvent>)subscription.Handler)(@event);
            } catch (Exception ex) {
                _faults.AddOrUpdate(subscription.Owner, 1, (_, count) => count + 1);
                if (FaultPolicy == HandlerFaultPolicy.Rethrow) {
                    throw new ModEventHandlerException(subscription.Owner, typeof(TEvent), ex);
                }
                _logger.Log(ModLogLevel.Error,
                    $"Handler for {typeof(TEvent).Name} owned by '{subscription.Owner}' threw; continuing with the next handler.", ex);
            }
        }
    }

    public bool HasSubscribers<TEvent>() where TEvent : class
        => _handlers.TryGetValue(typeof(TEvent), out var subscriptions) && subscriptions.Length > 0;

    /// <summary>Removes every subscription owned by <paramref name="owner"/>. Returns how many were removed.</summary>
    public int RemoveOwner(string owner) {
        var removed = 0;
        lock (_writeGate) {
            foreach (var (type, subscriptions) in _handlers) {
                var kept = subscriptions.Where(s => s.Owner != owner).ToArray();
                removed += subscriptions.Length - kept.Length;
                if (kept.Length == 0) {
                    _handlers.TryRemove(type, out _);
                } else if (kept.Length != subscriptions.Length) {
                    _handlers[type] = kept;
                }
            }
        }
        return removed;
    }

    public void Freeze() {
        lock (_writeGate) {
            _frozen = true;
        }
    }

    /// <summary>Subscriptions for <typeparamref name="TEvent"/> as (owner, priority), in dispatch order.</summary>
    public IReadOnlyList<(string Owner, int Priority)> Describe<TEvent>() where TEvent : class
        => _handlers.TryGetValue(typeof(TEvent), out var subscriptions)
            ? [.. subscriptions.Select(s => (s.Owner, s.Priority))]
            : [];

    private sealed record Subscription(string Owner, int OwnerOrder, int Priority, long Sequence, Delegate Handler) {

        public static readonly Comparison<Subscription> DispatchOrder = (left, right) => {
            var result = right.Priority.CompareTo(left.Priority);
            if (result != 0) {
                return result;
            }
            result = left.OwnerOrder.CompareTo(right.OwnerOrder);
            return result != 0 ? result : left.Sequence.CompareTo(right.Sequence);
        };

    }

}

/// <summary>The view of the bus handed to one owner (a mod, or the host).</summary>
internal sealed class ScopedEventBus(EventBus bus, string owner, int ownerOrder) : IEventBus {

    public string Owner { get; } = owner;

    public void Subscribe<TEvent>(Action<TEvent> handler, int priority = EventPriority.Normal) where TEvent : class
        => bus.Subscribe(Owner, ownerOrder, handler, priority);

    public void Publish<TEvent>(TEvent @event) where TEvent : class => bus.Publish(@event);

    public bool HasSubscribers<TEvent>() where TEvent : class => bus.HasSubscribers<TEvent>();

}
