using System;
using System.Collections.Generic;

namespace StateAlchemist.Contracts;

/// <summary>
/// What a contract machine's hooks do, whichever implementation runs it: record into a shared log and, for exception
/// hooks, return the resolution a test asks for. An implementation installs the exception hook only when
/// <see cref="HandleExceptions"/> is set — as a generated machine emits no <c>try</c>/<c>catch</c> unless the
/// application implements the hook.
/// </summary>
/// <param name="log">The log to record into, usually the context's.</param>
public sealed class ContractHooks(List<string> log)
{
    /// <summary>Whether the exception hooks are implemented.</summary>
    public bool HandleExceptions { get; init; }

    /// <summary>The resolution the exception hooks return, by phase. Defaults to <see cref="ExceptionResolution.Rethrow"/>.</summary>
    public Func<Phase, ExceptionResolution> Resolution { get; init; } = _ => ExceptionResolution.Rethrow;

    /// <summary>Run by an exception hook after recording, with the machine — for example to enqueue a recovery event.</summary>
    public Action<IMachine<byte>>? AfterException { get; init; }

    /// <summary>The machine, set by the harness once it is created.</summary>
    public IMachine<byte>? Machine { get; set; }

    /// <summary><c>OnTransitioned</c>.</summary>
    public void Transitioned(in TransitionInfo<byte> transition) => log.Add($"transitioned {transition.Transition}");

    /// <summary><c>OnUnhandled</c> for a value.</summary>
    public void UnhandledValue(Type state, byte value) => log.Add($"unhandled {value} in {state.Name}");

    /// <summary><c>OnUnhandled</c> for an event.</summary>
    public void UnhandledEvent(Type state, object e) => log.Add($"unhandled {e.GetType().Name} in {state.Name}");

    /// <summary><c>On{Phase}Exception</c>.</summary>
    public ExceptionResolution Exception(Exception exception, in TransitionInfo<byte> transition)
    {
        log.Add($"hook {transition.Phase}{(transition.State is null ? "" : " " + transition.State.Name)}: {exception.Message}");
        AfterException?.Invoke(Machine!);
        return Resolution(transition.Phase);
    }
}
