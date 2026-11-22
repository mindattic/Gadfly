using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Gadfly.Server.Sessions;

/// <summary>
/// Tells the components on this Blazor circuit that its own connection to the server was lost and has just come back
/// (GAD-T306). While the connection is down the server side can't do anything at all (there is no way to push a
/// render, and the built-in "Reconnecting…" status in app.css is the only thing the student sees); once it is back,
/// a live page should re-sync from the server's authoritative state rather than carry on from whatever it last knew,
/// which may be stale (the clock moved, a staff pause or extension happened, time ran out) while it was gone.
/// </summary>
public sealed class CircuitReconnectSignal
{
    private readonly List<Func<CancellationToken, Task>> _subscribers = [];
    private readonly object _gate = new();

    /// <summary>Calls <paramref name="onReconnected"/> whenever this circuit's connection comes back after being lost. Dispose to stop.</summary>
    public IDisposable Subscribe(Func<CancellationToken, Task> onReconnected)
    {
        lock (_gate) _subscribers.Add(onReconnected);
        return new Unsubscriber(this, onReconnected);
    }

    internal async Task RaiseAsync(CancellationToken ct)
    {
        Func<CancellationToken, Task>[] subscribers;
        lock (_gate) subscribers = [.. _subscribers];
        foreach (var subscriber in subscribers)
        {
            try { await subscriber(ct); }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // One page's own re-sync failing (it will try again on its next step, or the student reloads) must
                // never stop another page on the same circuit from getting its turn.
            }
        }
    }

    private sealed class Unsubscriber(CircuitReconnectSignal owner, Func<CancellationToken, Task> subscriber) : IDisposable
    {
        public void Dispose() { lock (owner._gate) owner._subscribers.Remove(subscriber); }
    }
}

/// <summary>Raises <see cref="CircuitReconnectSignal"/> when this circuit's connection, previously lost, comes back.</summary>
public sealed class ReconnectCircuitHandler(CircuitReconnectSignal signal) : CircuitHandler
{
    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken) =>
        signal.RaiseAsync(cancellationToken);
}
