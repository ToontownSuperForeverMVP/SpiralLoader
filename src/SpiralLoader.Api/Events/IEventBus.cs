namespace SpiralLoader.Api.Events;

/// <summary>
/// Typed, synchronous publish/subscribe bus shared by the host and every mod.
/// </summary>
/// <remarks>
/// <para>
/// Dispatch is by exact event type. Handlers run synchronously on the publishing thread, ordered by
/// priority (highest first), then mod load order, then registration order. In Imlight that thread is
/// usually an Akka.NET actor's: handlers must not block, must not keep references to actor state for
/// later use, and should hand long-running work off with <c>IActorRef.Tell</c> or a task.
/// </para>
/// <para>
/// Subscriptions are only accepted while mods initialize. Once the host finishes loading, the bus is
/// frozen and further <see cref="Subscribe{TEvent}"/> calls throw <see cref="RegistrationClosedException"/>.
/// A frozen bus is immutable, so publishing from many actors concurrently needs no locking.
/// </para>
/// </remarks>
public interface IEventBus {

    /// <summary>Adds a handler for <typeparamref name="TEvent"/>.</summary>
    /// <param name="handler">The handler. Exceptions it throws are logged against the owning mod.</param>
    /// <param name="priority">Higher runs earlier. See <see cref="EventPriority"/>.</param>
    void Subscribe<TEvent>(Action<TEvent> handler, int priority = EventPriority.Normal) where TEvent : class;

    /// <summary>Delivers <paramref name="event"/> to every handler of its exact type.</summary>
    void Publish<TEvent>(TEvent @event) where TEvent : class;

    /// <summary>
    /// True if anything handles <typeparamref name="TEvent"/>. Hosts use this to skip building event
    /// objects on hot paths when no mod is listening.
    /// </summary>
    bool HasSubscribers<TEvent>() where TEvent : class;

}

/// <summary>Conventional priorities for <see cref="IEventBus.Subscribe{TEvent}"/>.</summary>
public static class EventPriority {

    public const int Highest = 200;
    public const int High = 100;
    public const int Normal = 0;
    public const int Low = -100;
    public const int Lowest = -200;

    /// <summary>For handlers that only observe the final state and must not modify the event.</summary>
    public const int Monitor = int.MinValue;

}

/// <summary>
/// Base for events whose default action a handler may cancel. Once cancelled, lower-priority
/// handlers are skipped and the host does not perform the default action.
/// </summary>
public abstract class CancellableEvent {

    public bool IsCancelled { get; private set; }

    public void Cancel() => IsCancelled = true;

}

/// <summary>Thrown when registering after the host has closed registrations.</summary>
public sealed class RegistrationClosedException(string message) : InvalidOperationException(message);

/// <summary>
/// An event bus that drops everything. Hosts use it when no loader is attached, so call sites behave
/// exactly as they would without SpiralLoader.
/// </summary>
public sealed class NullEventBus : IEventBus {

    public static NullEventBus Instance { get; } = new();

    private NullEventBus() { }

    public void Subscribe<TEvent>(Action<TEvent> handler, int priority = EventPriority.Normal) where TEvent : class
        => throw new RegistrationClosedException("No mod loader is attached to this host.");

    public void Publish<TEvent>(TEvent @event) where TEvent : class { }

    public bool HasSubscribers<TEvent>() where TEvent : class => false;

}
