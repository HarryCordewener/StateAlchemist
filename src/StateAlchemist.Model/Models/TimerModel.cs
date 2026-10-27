using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>
/// One timer of a machine: the <c>[After]</c> transitions that share a source state and a delay. It is armed when
/// its source is entered and cancelled when the source is exited; when it fires, its candidates are tried as the
/// candidates of any trigger are: guarded ones by <see cref="TransitionModel.Order"/>, then the unguarded one.
/// </summary>
/// <param name="Index">Its position in <see cref="Timers.Of"/>.</param>
/// <param name="Source">The state it belongs to.</param>
/// <param name="Trigger">Its trigger: a fixed delay, or a transition's <c>Delay</c> method.</param>
/// <param name="Candidates">Its transitions, in the order they are tried.</param>
public sealed record TimerModel(int Index, int Source, TriggerModel Trigger, IReadOnlyList<TransitionModel> Candidates)
{
    /// <summary>The <c>Delay</c> method that computes the delay, or <see langword="null"/> for a fixed delay.</summary>
    public MethodModel? Delay => Candidates[0].Delay;

    /// <summary>The fixed delay in milliseconds, when <see cref="Delay"/> is <see langword="null"/>.</summary>
    public long Milliseconds => Trigger.Low;

    /// <summary>The name a snapshot records it by: its first transition's.</summary>
    public string Name => Candidates[0].Name;
}

/// <summary>Finds a machine's timers, and reads <c>[After]</c> the same way for every front-end.</summary>
public static class Timers
{
    /// <summary>Every timer, by source state and then by declaration.</summary>
    public static IReadOnlyList<TimerModel> Of(MachineModel model) => model.Transitions
        .Where(t => t.IsTimer)
        .GroupBy(t => (t.Source, t.Trigger))
        .OrderBy(g => g.Key.Source)
        .ThenBy(g => g.Min(t => t.Index))
        .Select((g, i) => new TimerModel(i, g.Key.Source, g.Key.Trigger,
            g.Where(t => t.IsGuarded).OrderBy(t => t.Order).ThenBy(t => t.Index).Concat(g.Where(t => !t.IsGuarded).Take(1)).ToList()))
        .ToList();

    /// <summary>The timers whose source is one of <paramref name="states"/>, in the order given.</summary>
    public static IEnumerable<TimerModel> On(IReadOnlyList<TimerModel> timers, IEnumerable<int> states) =>
        states.SelectMany(state => timers.Where(t => t.Source == state));

    /// <summary>
    /// The trigger an <c>[After]</c> declares, or <see langword="null"/> with <paramref name="problem"/> saying why
    /// not, worded to follow the transition's name in <c>SALCH0106</c>.
    /// </summary>
    /// <param name="name">The transition.</param>
    /// <param name="milliseconds">Its <c>Milliseconds</c>; zero when not set.</param>
    /// <param name="seconds">Its <c>Seconds</c>; zero when not set.</param>
    /// <param name="hasDelay">Whether the transition declares a <c>Delay</c> method.</param>
    /// <param name="problem">What is wrong, when the result is <see langword="null"/>.</param>
    public static TriggerModel? Trigger(string name, int milliseconds, int seconds, bool hasDelay, out string? problem)
    {
        problem = null;
        if (milliseconds != 0 && seconds != 0)
        {
            problem = "sets both Milliseconds and Seconds on [After]; set one";
            return null;
        }

        if (milliseconds < 0 || seconds < 0)
        {
            problem = "has a negative [After] delay";
            return null;
        }

        var total = milliseconds != 0 ? milliseconds : seconds * 1000L;
        if (total == 0)
        {
            if (hasDelay)
            {
                return TriggerModel.TimerByDelay(name);
            }

            problem = "has an [After] with no delay: set Milliseconds or Seconds, or declare a Delay method";
            return null;
        }

        if (hasDelay)
        {
            problem = "sets an [After] delay and declares a Delay method; use one";
            return null;
        }

        if (total > TriggerModel.MaxDelayMilliseconds)
        {
            problem = $"has an [After] delay longer than {TriggerModel.MaxDelayMilliseconds} milliseconds";
            return null;
        }

        return TriggerModel.Timer(total);
    }
}
