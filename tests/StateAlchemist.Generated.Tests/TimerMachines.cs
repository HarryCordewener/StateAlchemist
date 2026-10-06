using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Timing;

namespace StateAlchemist.Generated.Tests;

// Timers in the shapes the timer contracts leave out: no async decision, so a Checked machine has no pending
// decision to hand a firing to; Unchecked; a bounded inbox; and the system clock.

[Module]
public static class KettleModule
{
    /// <summary>1: start recording, which times out after a second.</summary>
    [Transition(From = typeof(Idle), To = typeof(Recording)), On(1)]
    public static void Record()
    {
    }

    [Transition(From = typeof(Recording), To = typeof(TimedOut)), After(Seconds = 1)]
    public static void GiveUp(RecordingContext context) => context.Record("gave up");

    /// <summary>3: choose, which moves on after 50 milliseconds and opens the gate.</summary>
    [Transition(From = typeof(Idle), To = typeof(Choosing)), On(3)]
    public static void Choose()
    {
    }

    [Transition(From = typeof(Choosing), To = typeof(Left)), After(Milliseconds = 50)]
    public static void Chosen(RecordingContext context) => context.Gate.TrySetResult();

    [Transition(From = typeof(TimeRoot)), OnAny]
    public static void Ignore()
    {
    }
}

[Module]
public static class SlowKettleModule
{
    /// <summary>2: wait, holding the call open until the gate opens, while Waiting's timer runs.</summary>
    [Transition(From = typeof(Idle), To = typeof(Waiting)), On(2)]
    public static class Wait
    {
        public static Task CompletedAsync(RecordingContext context) => context.Gate.Task;
    }

    [Transition(From = typeof(Waiting), To = typeof(TimedOut)), After(Seconds = 1)]
    public static void TimedOut(RecordingContext context) => context.Record("timed out");

    [Transition(From = typeof(TimeRoot)), OnAny]
    public static void Ignore(RecordingContext context) => context.Record("ignored");
}

[Machine(Root = typeof(TimeRoot), Value = typeof(byte), Context = typeof(RecordingContext))]
[Include(typeof(KettleModule))]
public sealed partial class KettleMachine;

[Machine(Root = typeof(TimeRoot), Value = typeof(byte), Context = typeof(RecordingContext), Concurrency = Concurrency.Unchecked)]
[Include(typeof(KettleModule))]
public sealed partial class KettleUncheckedMachine;

[Machine(Root = typeof(TimeRoot), Value = typeof(byte), Context = typeof(RecordingContext), Telemetry = true)]
[Include(typeof(KettleModule))]
public sealed partial class KettleTelemetryMachine;

[Machine(Root = typeof(TimeRoot), Value = typeof(byte), Context = typeof(RecordingContext))]
[Include(typeof(SlowKettleModule))]
public sealed partial class SlowKettleMachine;

[Machine(Root = typeof(TimeRoot), Value = typeof(byte), Context = typeof(RecordingContext), Concurrency = Concurrency.Serialized, InboxCapacity = 1)]
[Include(typeof(SlowKettleModule))]
public sealed partial class SlowKettleBoundedMachine;
