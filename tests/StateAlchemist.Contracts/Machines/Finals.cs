namespace StateAlchemist.Contracts.Machines.Finals;

// FinRoot ─┬─ Idle [Initial]
//          ├─ Job ─┬─ Working [Initial]
//          │       └─ Finished [Final]
//          ├─ Review
//          ├─ Gate
//          ├─ LoopA
//          ├─ LoopB
//          └─ Closed [Final]

public struct FinRoot : IRootState
{
}

[Initial]
public struct Idle : IState<FinRoot>
{
}

public struct Job : IState<FinRoot>
{
}

[Initial]
public struct Working : IState<Job>
{
}

[Final]
public struct Finished : IState<Job>
{
}

public struct Review : IState<FinRoot>
{
}

public struct Gate : IState<FinRoot>
{
}

public struct LoopA : IState<FinRoot>
{
}

public struct LoopB : IState<FinRoot>
{
}

[Final]
public struct Closed : IState<FinRoot>
{
}

/// <summary>Final states, a done transition, eventless transitions and completion.</summary>
[Module]
public static class FinalModule
{
    [Transition(From = typeof(Idle), To = typeof(Job)), On(1)]
    public static void Begin(in Idle from)
    {
    }

    [Transition(From = typeof(Working), To = typeof(Finished)), On(2)]
    public static void Finish(in Working from)
    {
    }

    [Transition(From = typeof(Job), To = typeof(Review)), OnDone]
    public static class JobDone
    {
        public static void Completed(RecordingContext context) => context.Record("job done");
    }

    [Transition(From = typeof(Review), To = typeof(Gate)), On(3)]
    public static void Pass(in Review from)
    {
    }

    [Transition(From = typeof(Gate), To = typeof(Closed)), Always]
    public static class Through
    {
        public static bool Guard(RecordingContext context) => context.Allow.Contains("Gate");

        public static void Completed(RecordingContext context) => context.Record("through");
    }

    [Transition(From = typeof(Gate)), On(4)]
    public static void Spin(ref Gate self)
    {
    }

    [Transition(From = typeof(Idle), To = typeof(LoopA)), On(9)]
    public static void ToLoop(in Idle from)
    {
    }

    [Transition(From = typeof(LoopA), To = typeof(LoopB)), Always]
    public static class AToB
    {
        public static bool Guard(RecordingContext context) => context.Allow.Contains("Loop");
    }

    [Transition(From = typeof(LoopB), To = typeof(LoopA)), Always]
    public static class BToA
    {
        public static bool Guard(RecordingContext context) => context.Allow.Contains("Loop");
    }

    [Transition(From = typeof(FinRoot)), On(7)]
    public static void Ping(RecordingContext context) => context.Record("ping");

    [Entered(typeof(Closed))]
    public static void ClosedEntered(RecordingContext context) => context.Record("entered Closed");

    [Exited(typeof(FinRoot))]
    public static void RootExited(RecordingContext context) => context.Record("exited FinRoot");
}
