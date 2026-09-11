using System.Threading.Tasks;

namespace StateAlchemist.Contracts.Machines.Recording;

/// <summary>A machine that records every step, for the order-of-operations and data-lifetime contracts.</summary>
[Module]
public static class RecorderModule
{
    /// <summary>1: to a sibling; the shared parent stays and is written.</summary>
    [Transition(From = typeof(A1), To = typeof(A2)), On(1)]
    public static class Sibling
    {
        public static void Transform(in A1 from, ref A parent, ref A2 to)
        {
            parent.Value += 1;
            to.Value = from.Value + 10;
        }

        public static void Completed(RecordingContext context) => context.Record("completed Sibling");
    }

    /// <summary>2: to a cousin; both parents change.</summary>
    [Transition(From = typeof(A1), To = typeof(B1)), On(2)]
    public static class Cousin
    {
        public static void Transform(in A1 from, in A parent, ref Root root, ref B b, ref B1 to)
        {
            root.Counter += from.Value + parent.Value;
            b.Value = 7;
            to.Count = 1;
        }

        public static void Completed(RecordingContext context) => context.Record("completed Cousin");
    }

    /// <summary>3: a stay.</summary>
    [Transition(From = typeof(A1)), On(3)]
    public static void Stay(ref A1 self) => self.Value++;

    /// <summary>4: a re-entry: the old data in, the new data out.</summary>
    [Transition(From = typeof(A1), To = typeof(A1)), On(4)]
    public static void Restart(in A1 from, ref A1 to) => to.Value = from.Value * 100;

    /// <summary>5: declared on A, into A1 — a move from A2, a start-over from A1.</summary>
    [Transition(From = typeof(A), To = typeof(A1)), On(5)]
    public static void Home(ref A1 to) => to.Value = 50;

    /// <summary>6: to the root, which is never exited.</summary>
    [Transition(From = typeof(A), To = typeof(Root)), On(6)]
    public static void ToRoot(ref Root root) => root.Counter = -1;

    /// <summary>7: from B1 back into A.</summary>
    [Transition(From = typeof(B1), To = typeof(A)), On(7)]
    public static void Back(in B1 from)
    {
    }

    /// <summary>8: a stay that captures into a buffer B1 keeps across entries.</summary>
    [Transition(From = typeof(B1)), On(8)]
    public static void Capture(ref B1 self, byte value)
    {
        self.Buffer ??= new int[8];
        self.Buffer[self.Count++ % 8] = value;
    }

    /// <summary>9: an action that enqueues an event.</summary>
    [Transition(From = typeof(A2)), On(9)]
    public static class Echo
    {
        public static void Completed(RecordingContext context)
        {
            context.Record("completed Echo");
            context.Machine!.Enqueue(new Ping { Amount = 100 });
        }
    }

    /// <summary>10: an action that fires its own machine — a mistake the machine must catch.</summary>
    [Transition(From = typeof(A1)), On(10)]
    public static class Reenter
    {
        public static async ValueTask CompletedAsync(RecordingContext context) => await context.Machine!.FireAsync((byte)3);
    }

    /// <summary>11: an action held open until a test releases it.</summary>
    [Transition(From = typeof(A1)), On(11)]
    public static class Held
    {
        public static async ValueTask CompletedAsync(RecordingContext context)
        {
            context.Record("held");
            await context.Gate.Task;
        }
    }

    /// <summary>Ping: declared on the root, so it reaches every state.</summary>
    [Transition(From = typeof(Root)), OnEvent(typeof(Ping))]
    public static class Pinged
    {
        public static void Transform(ref Root root, in Ping ping) => root.Counter += ping.Amount;

        public static void Completed(RecordingContext context, Ping ping) => context.Record($"pinged {ping.Amount}");
    }

    [Entered(typeof(Root))]
    public static void EnterRoot(RecordingContext context) => context.Record("entered Root");

    [Entered(typeof(A))]
    public static void EnterA(RecordingContext context) => context.Record("entered A");

    [Entered(typeof(A1))]
    public static void EnterA1(RecordingContext context, A1 state) => context.Record($"entered A1 ({state.Value})");

    [Entered(typeof(A2))]
    public static void EnterA2(RecordingContext context) => context.Record("entered A2");

    [Entered(typeof(B))]
    public static void EnterB(RecordingContext context) => context.Record("entered B");

    [Entered(typeof(B1))]
    public static void EnterB1(RecordingContext context) => context.Record("entered B1");

    [Exited(typeof(Root))]
    public static void ExitRoot(RecordingContext context) => context.Record("exited Root");

    [Exited(typeof(A))]
    public static void ExitA(RecordingContext context, A state) => context.Record($"exited A ({state.Value})");

    [Exited(typeof(A1))]
    public static void ExitA1(RecordingContext context, A1 state) => context.Record($"exited A1 ({state.Value})");

    [Exited(typeof(A2))]
    public static void ExitA2(RecordingContext context) => context.Record("exited A2");

    [Exited(typeof(B))]
    public static void ExitB(RecordingContext context) => context.Record("exited B");

    [Exited(typeof(B1))]
    public static void ExitB1(RecordingContext context) => context.Record("exited B1");
}

/// <summary>A second module adding an action to a state the first owns, ordered after it.</summary>
[Module]
public static class RecorderExtras
{
    [Exited(typeof(A1), Order = 1)]
    public static void AlsoExitA1(RecordingContext context) => context.Record("exited A1 (extra)");
}
