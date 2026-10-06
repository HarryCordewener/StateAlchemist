using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace StateAlchemist.Hosting;

/// <summary>
/// Runs a machine for the lifetime of a host: starts it when the host starts, fires every input written to its
/// <see cref="MachineInbox{TMachine,TInput}"/> one at a time, and stops it when the host stops.
/// </summary>
/// <remarks>
/// <para>
/// The machine's <c>StartAsync</c> runs inside the host's start, so the host is not started until the initial
/// path's <c>[Entered]</c> actions have run, and one that throws fails the start.
/// </para>
/// <para>
/// Each input is awaited before the next is read. <c>FireAsync</c> completes when its input has been processed,
/// including any decision it started, so a bounded inbox fills while the machine is busy and its writers wait.
/// </para>
/// <para>
/// On shutdown the inbox stops accepting inputs, those already queued are fired, and then the machine's
/// <c>StopAsync</c> runs its <c>[Exited]</c> actions. If the host's shutdown timeout ends first, the inputs still
/// queued are dropped and the machine is stopped anyway, which cancels a pending decision.
/// </para>
/// <para>
/// An exception from firing ends the service, and the host does what its
/// <c>HostOptions.BackgroundServiceExceptionBehavior</c> says: by default, it stops.
/// </para>
/// </remarks>
/// <typeparam name="TMachine">The machine.</typeparam>
/// <typeparam name="TValue">The machine's value type.</typeparam>
/// <typeparam name="TInput">What the inbox holds.</typeparam>
public sealed class MachineHostedService<TMachine, TValue, TInput> : BackgroundService
    where TMachine : class, IMachine<TValue>
    where TValue : struct
{
    private readonly TMachine _machine;
    private readonly MachineInbox<TMachine, TInput> _inbox;
    private readonly Func<TMachine, TInput, ValueTask> _fire;

    /// <summary>Creates the service. The machine must not have been started.</summary>
    /// <param name="machine">The machine to run.</param>
    /// <param name="inbox">Where its inputs come from.</param>
    /// <param name="fire">Fires one input: for a value, <c>(m, v) =&gt; m.FireAsync(v)</c>.</param>
    public MachineHostedService(TMachine machine, MachineInbox<TMachine, TInput> inbox, Func<TMachine, TInput, ValueTask> fire)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(fire);
        _machine = machine;
        _inbox = inbox;
        _fire = fire;
    }

    /// <summary>Starts the machine, then starts reading the inbox.</summary>
    /// <param name="cancellationToken">The host's start is being abandoned.</param>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _machine.StartAsync().ConfigureAwait(false);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fires what is queued, then stops the machine.</summary>
    /// <param name="cancellationToken">The host's shutdown timeout: when it fires, inputs still queued are dropped.</param>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _inbox.Complete();
        try
        {
            if (ExecuteTask is { } pump)
            {
                await WhenCompleteOrCancelled(pump, cancellationToken).ConfigureAwait(false);
            }

            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (_machine.Status == MachineStatus.Running)
            {
                await _machine.StopAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _inbox.Reader;
        try
        {
            while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                while (!stoppingToken.IsCancellationRequested && reader.TryRead(out var input))
                {
                    await _fire(_machine, input).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The shutdown timeout ended the wait for the next input.
        }
        catch (MachineNotRunningException) when (stoppingToken.IsCancellationRequested)
        {
            // The machine was stopped under an input that was waiting on a decision.
        }
    }

    // Waits for the task without observing its exception: a failed pump has already been reported by the host.
    private static async Task WhenCompleteOrCancelled(Task task, CancellationToken cancellationToken)
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), cancelled))
        {
            await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false);
        }
    }
}
