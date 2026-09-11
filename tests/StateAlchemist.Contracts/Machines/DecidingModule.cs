using System.Threading;
using System.Threading.Tasks;

namespace StateAlchemist.Contracts.Machines.Deciding;

/// <summary>Decisions, synchronous and async, recording what they do; the test answers the async ones through <see cref="RecordingContext.Answer"/>.</summary>
[Module]
public static class DecidingModule
{
    /// <summary>1: counts, from anywhere — the "rest of the input" a pending decision holds back.</summary>
    [Transition(From = typeof(DecideRoot)), On(1)]
    public static void Tick(ref DecideRoot root, RecordingContext context)
    {
        root.Ticks++;
        context.Record("tick");
    }

    /// <summary>7: another question.</summary>
    [Transition(From = typeof(Asking)), On(7)]
    public static void More(ref Asking self) => self.Question++;

    /// <summary>4: back to asking, from anywhere.</summary>
    [Transition(From = typeof(DecideRoot), To = typeof(Asking)), On(4)]
    public static void Again()
    {
    }

    /// <summary>2: an async decision the test answers. A hang-up interrupts it.</summary>
    [Decision(From = typeof(Asking), Handle = new[] { typeof(Hangup) }), On(2)]
    public static class Ask
    {
        public static async ValueTask<Verdict> DecideAsync(RecordingContext context, Asking asking, CancellationToken cancellation)
        {
            context.Record($"deciding {asking.Question}");
            context.Deciding.TrySetResult(cancellation);
            return (Verdict)await context.Answer.Task;
        }

        [To(typeof(Account))]
        public static void Complete(in Asking from, ref Account to, Accept outcome) => to.Name = outcome.Name;

        [To(typeof(Refused))]
        public static void Complete(in Asking from, ref Refused to, Reject outcome) => to.Code = outcome.Code + from.Question;

        public static ValueTask CompletedAsync(RecordingContext context, Account account, Accept outcome)
        {
            context.Record($"welcome {outcome.Name} as {account.Name}");
            return default;
        }

        public static void Completed(RecordingContext context, Reject outcome) => context.Record($"refused {outcome.Code}");
    }

    /// <summary>3: a synchronous decision: it answers from the data it is given, at once.</summary>
    [Decision(From = typeof(Asking)), On(3)]
    public static class Quick
    {
        public static Verdict Decide(RecordingContext context, Asking asking)
        {
            context.Record($"quick {asking.Question}");
            return asking.Question > 0 ? new Accept("quick") : new Reject(3);
        }

        [To(typeof(Account))]
        public static void Complete(ref Account to, Accept outcome) => to.Name = outcome.Name;

        [To(typeof(Refused))]
        public static void Complete(ref Refused to, Reject outcome) => to.Code = outcome.Code;
    }

    /// <summary>5: a decision that fires its own machine instead of enqueueing.</summary>
    [Decision(From = typeof(Asking)), On(5)]
    public static class Loop
    {
        public static async ValueTask<Verdict> DecideAsync(RecordingContext context)
        {
            await context.Machine!.FireAsync((byte)1);
            return new Accept("never");
        }

        [To(typeof(Account))]
        public static void Complete(ref Account to, Accept outcome) => to.Name = outcome.Name;

        [To(typeof(Refused))]
        public static void Complete(ref Refused to, Reject outcome) => to.Code = outcome.Code;
    }

    /// <summary>6: a decision that enqueues, which is how code inside the machine asks for more.</summary>
    [Decision(From = typeof(Asking)), On(6)]
    public static class Enqueuer
    {
        public static async ValueTask<Verdict> DecideAsync(RecordingContext context)
        {
            await Task.Yield();
            context.Machine!.Enqueue(new Nudge());
            return new Accept("queued");
        }

        [To(typeof(Account))]
        public static void Complete(ref Account to, Accept outcome) => to.Name = outcome.Name;

        [To(typeof(Refused))]
        public static void Complete(ref Refused to, Reject outcome) => to.Code = outcome.Code;

        public static void Completed(RecordingContext context, Accept outcome) => context.Record($"welcome {outcome.Name}");
    }

    /// <summary>8: from Account, an async decision nothing recovers from when it fails.</summary>
    [Decision(From = typeof(Account)), On(8)]
    public static class Recheck
    {
        public static async ValueTask<Verdict> DecideAsync(RecordingContext context) => (Verdict)await context.Answer.Task;

        [To(typeof(Account))]
        public static void Complete(ref Account to, Accept outcome) => to.Name = outcome.Name;

        [To(typeof(Refused))]
        public static void Complete(ref Refused to, Reject outcome) => to.Code = outcome.Code;
    }

    /// <summary>9: a decision that enqueues the hang-up it handles, then waits for the test.</summary>
    [Decision(From = typeof(Asking), Handle = new[] { typeof(Hangup) }), On(9)]
    public static class HangUpOnItself
    {
        public static async ValueTask<Verdict> DecideAsync(RecordingContext context, CancellationToken cancellation)
        {
            context.Deciding.TrySetResult(cancellation);
            await Task.Yield();
            context.Machine!.Enqueue(new Hangup());
            return (Verdict)await context.Answer.Task;
        }

        [To(typeof(Account))]
        public static void Complete(ref Account to, Accept outcome) => to.Name = outcome.Name;

        [To(typeof(Refused))]
        public static void Complete(ref Refused to, Reject outcome) => to.Code = outcome.Code;
    }

    /// <summary>A hang-up returns to asking from anywhere: from the pending state, that leaves it.</summary>
    [Transition(From = typeof(DecideRoot), To = typeof(Asking)), OnEvent(typeof(Hangup))]
    public static void HungUp(RecordingContext context) => context.Record("hung up");

    /// <summary>A nudge is noted wherever it arrives.</summary>
    [Transition(From = typeof(DecideRoot)), OnEvent(typeof(Nudge))]
    public static void Nudged(RecordingContext context) => context.Record("nudged");

    /// <summary>A failed decision from Asking is refused with the reason.</summary>
    [Transition(From = typeof(Asking), To = typeof(Refused)), OnEvent(typeof(DecisionFailed))]
    public static void Failed(in DecisionFailed failed, ref Refused to, RecordingContext context)
    {
        to.Code = -1;
        context.Record($"failed {failed.Decision}: {failed.Exception.Message}");
    }
}
