using System;
using System.Threading;
using System.Threading.Tasks;

namespace StateAlchemist.Contracts.Machines.Timing;

/// <summary>Timers: armed on entry, cancelled on exit, and fired through the inbox. The test drives the clock.</summary>
[Module]
public static class TimingModule
{
    /// <summary>0: back to idle, from anywhere.</summary>
    [Transition(From = typeof(TimeRoot), To = typeof(Idle)), On(0)]
    public static void Reset(RecordingContext context) => context.Record("reset");

    /// <summary>The initial state's own timer, armed by StartAsync.</summary>
    [Transition(From = typeof(Idle), To = typeof(TimedOut)), After(Seconds = 3600)]
    public static class Dozed
    {
        public static void Completed(RecordingContext context) => context.Record("dozed");
    }

    /// <summary>1: start recording.</summary>
    [Transition(From = typeof(Idle), To = typeof(Recording)), On(1)]
    public static void Record(RecordingContext context) => context.Record("record");

    /// <summary>3: record again: a re-entry, which restarts the timer.</summary>
    [Transition(From = typeof(Recording), To = typeof(Recording)), On(3)]
    public static void Retake(RecordingContext context) => context.Record("retake");

    /// <summary>4: another take: a stay, which leaves the timer running.</summary>
    [Transition(From = typeof(Recording)), On(4)]
    public static void Take(ref Recording self) => self.Takes++;

    /// <summary>5: pause, a move inside Recording, which leaves Recording's timer running.</summary>
    [Transition(From = typeof(Rolling), To = typeof(Paused)), On(5)]
    public static void Pause()
    {
    }

    [Transition(From = typeof(Recording), To = typeof(TimedOut)), After(Seconds = 60)]
    public static class GiveUp
    {
        public static void Transform(ref TimeRoot root, in Recording from) => root.Fired++;

        public static void Completed(RecordingContext context, TimeRoot root) => context.Record($"gave up {root.Fired}");
    }

    /// <summary>6: wait for as many seconds as the value's transform says.</summary>
    [Transition(From = typeof(Idle), To = typeof(Waiting)), On(6)]
    public static void Wait(ref Waiting to) => to.Seconds = 5;

    /// <summary>11: wait no time at all: the timer is due as Waiting is entered.</summary>
    [Transition(From = typeof(Idle), To = typeof(Waiting)), On(11)]
    public static void WaitNoTime(ref Waiting to) => to.Seconds = 0;

    [Transition(From = typeof(Waiting), To = typeof(Idle)), After]
    public static class Waited
    {
        public static TimeSpan Delay(in Waiting self) => TimeSpan.FromSeconds(self.Seconds);

        public static void Completed(RecordingContext context) => context.Record("waited");
    }

    /// <summary>7: choose, by a guard, when the timer fires.</summary>
    [Transition(From = typeof(Idle), To = typeof(Choosing)), On(7)]
    public static void Choose()
    {
    }

    [Transition(From = typeof(Choosing), To = typeof(Left), Order = 1), After(Seconds = 10)]
    public static class GoLeft
    {
        public static bool Guard(RecordingContext context) => context.Allow.Contains("left");

        public static void Completed(RecordingContext context) => context.Record("left");
    }

    [Transition(From = typeof(Choosing), To = typeof(Right)), After(Seconds = 10)]
    public static class GoRight
    {
        public static void Completed(RecordingContext context) => context.Record("right");
    }

    /// <summary>Only allowed: when the guard refuses, nothing happens.</summary>
    [Transition(From = typeof(Choosing), To = typeof(TimedOut)), After(Seconds = 5)]
    public static class Maybe
    {
        public static bool Guard(RecordingContext context) => context.Allow.Contains("maybe");

        public static void Completed(RecordingContext context) => context.Record("maybe");
    }

    /// <summary>8: a state whose timer's transition throws.</summary>
    [Transition(From = typeof(Idle), To = typeof(Exploding)), On(8)]
    public static void Arm()
    {
    }

    [Transition(From = typeof(Exploding), To = typeof(TimedOut)), After(Milliseconds = 1000)]
    public static class Explode
    {
        public static void Transform(RecordingContext context) => context.Record("explode");
    }

    /// <summary>9: ask, then decide: the timer fires while the decision is pending.</summary>
    [Transition(From = typeof(Idle), To = typeof(Asking)), On(9)]
    public static void StartAsking()
    {
    }

    [Decision(From = typeof(Asking)), On(2)]
    public static class Ask
    {
        public static async ValueTask<Answer> DecideAsync(RecordingContext context, CancellationToken cancellation)
        {
            context.Deciding.TrySetResult(cancellation);
            return (Answer)await context.Answer.Task;
        }

        [To(typeof(Left))]
        public static void Complete(Yes outcome)
        {
        }

        [To(typeof(Right))]
        public static void Complete(No outcome)
        {
        }
    }

    [Transition(From = typeof(Asking), To = typeof(TimedOut)), After(Seconds = 30)]
    public static class StopAsking
    {
        public static void Completed(RecordingContext context) => context.Record("stopped asking");
    }

    /// <summary>The other values do nothing.</summary>
    [Transition(From = typeof(TimeRoot)), OnAny]
    public static void Ignore()
    {
    }
}
