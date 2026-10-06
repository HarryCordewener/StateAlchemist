using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace StateAlchemist.Hosting;

/// <summary>
/// The queue a hosted machine is fed from. Anything can write to it; one <see cref="MachineHostedService{TMachine,TValue,TInput}"/>
/// reads it and fires each input in turn, so the machine itself only ever has one caller.
/// </summary>
/// <remarks>
/// <typeparamref name="TMachine"/> is part of the type so that two machines with the same input type are two
/// services: a producer asks for the inbox of the machine it means.
/// </remarks>
/// <typeparam name="TMachine">The machine the inputs are for.</typeparam>
/// <typeparam name="TInput">What is written: the machine's value type, an event, or anything the service's fire delegate understands.</typeparam>
public sealed class MachineInbox<TMachine, TInput>
{
    private readonly Channel<TInput> _channel;

    /// <summary>An inbox with no limit: writing never waits.</summary>
    public MachineInbox()
    {
        _channel = Channel.CreateUnbounded<TInput>(new UnboundedChannelOptions { SingleReader = true });
    }

    /// <summary>
    /// An inbox that holds at most <paramref name="capacity"/> inputs. While it is full, <see cref="WriteAsync"/>
    /// waits and <see cref="TryWrite"/> returns <see langword="false"/>, which carries the machine's backpressure
    /// back to the producers.
    /// </summary>
    /// <param name="capacity">The most inputs waiting at once. At least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is less than 1.</exception>
    public MachineInbox(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _channel = Channel.CreateBounded<TInput>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    /// <summary>Queues an input, waiting for room if the inbox is bounded and full.</summary>
    /// <param name="input">The input.</param>
    /// <param name="cancellationToken">Stops waiting for room.</param>
    /// <returns>Completes when the input is queued, not when the machine has processed it.</returns>
    /// <exception cref="ChannelClosedException">The host is stopping or has stopped.</exception>
    public ValueTask WriteAsync(TInput input, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(input, cancellationToken);

    /// <summary>Queues an input if there is room now.</summary>
    /// <param name="input">The input.</param>
    /// <returns>Whether it was queued: <see langword="false"/> when a bounded inbox is full, or the host is stopping.</returns>
    public bool TryWrite(TInput input) => _channel.Writer.TryWrite(input);

    internal ChannelReader<TInput> Reader => _channel.Reader;

    /// <summary>No more inputs are accepted; those already queued are still read.</summary>
    internal void Complete() => _channel.Writer.TryComplete();
}
