using System.Threading;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines.Deciding;
using StateAlchemist.Samples.Performance;

namespace StateAlchemist.Generated.Tests;

// Machines for the races: threads firing at once, a stop arriving mid-flight, a bounded inbox with a decision, and
// two disposals. Each exists because a bug reached 1.0.0 that nothing here could have caught.

/// <summary>Serialized, and everything about it synchronous: what <c>Fire</c> is for, from several threads.</summary>
[Machine(Root = typeof(Root), Value = typeof(byte), Context = typeof(Counters), Concurrency = Concurrency.Serialized)]
[Include(typeof(SyncModule))]
public sealed partial class SyncSerializedMachine;

/// <summary>What the bounded machine answers with, and what it counted while it waited.</summary>
public sealed class Gatekeeper
{
    public TaskCompletionSource<Verdict> Answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Set when the decision is pending, so a test can send an event it is known to be waiting through.</summary>
    public TaskCompletionSource<bool> Deciding = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Nudges;

    public int Exits;
}

[Module]
public static class BoundedDecisionModule
{
    /// <summary>1: waits for the test, and accepts nudges meanwhile without ending.</summary>
    [Decision(From = typeof(Asking), Handle = new[] { typeof(Nudge) }), On(1)]
    public static class Wait
    {
        public static async ValueTask<Verdict> DecideAsync(Gatekeeper gate)
        {
            gate.Deciding.TrySetResult(true);
            return await gate.Answer.Task;
        }

        [To(typeof(Account))]
        public static void Complete(ref Account to, Accept outcome) => to.Name = outcome.Name;

        [To(typeof(Refused))]
        public static void Complete(ref Refused to, Reject outcome) => to.Code = outcome.Code;
    }

    /// <summary>A nudge is counted where it lands. A stay, so the decision it interrupts keeps waiting.</summary>
    [Transition(From = typeof(DecideRoot)), OnEvent(typeof(Nudge))]
    public static void Noted(Gatekeeper gate) => gate.Nudges++;

    /// <summary>2: back to asking, so the next round can ask again.</summary>
    [Transition(From = typeof(DecideRoot), To = typeof(Asking)), On(2)]
    public static void Again()
    {
    }

    [Transition(From = typeof(Asking), To = typeof(Refused)), OnEvent(typeof(DecisionFailed))]
    public static void Failed(ref Refused to) => to.Code = -1;

    [Transition(From = typeof(DecideRoot)), OnAny]
    public static void Ignore()
    {
    }
}

/// <summary>A bounded inbox and a decision: the combination in which every handled event used to cost a slot.</summary>
[Machine(Root = typeof(DecideRoot), Value = typeof(byte), Context = typeof(Gatekeeper), Concurrency = Concurrency.Serialized, InboxCapacity = 2)]
[Include(typeof(BoundedDecisionModule))]
public sealed partial class BoundedDecidingMachine;

public struct Latch : IRootState
{
}

[Initial]
public struct Closed : IState<Latch>
{
}

[Module]
public static class ExitCountModule
{
    /// <summary>Counted with Interlocked because the point of the test is that two threads are here at once.</summary>
    [Exited(typeof(Closed))]
    public static void Left(Gatekeeper gate) => Interlocked.Increment(ref gate.Exits);

    [Transition(From = typeof(Latch)), OnAny]
    public static void Ignore()
    {
    }
}

/// <summary>Nothing but an exit action, so a disposal that runs twice is visible.</summary>
[Machine(Root = typeof(Latch), Value = typeof(byte), Context = typeof(Gatekeeper), Concurrency = Concurrency.Serialized)]
[Include(typeof(ExitCountModule))]
public sealed partial class ExitCountMachine;
