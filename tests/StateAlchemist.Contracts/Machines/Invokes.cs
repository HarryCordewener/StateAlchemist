namespace StateAlchemist.Contracts.Machines.Invokes;

// InvRoot ─┬─ InvIdle [Initial]
//          ├─ InvRunning   invokes a worker, forwarding
//          ├─ InvParallel  invokes two workers, not forwarding: parallel regions
//          └─ InvJoined
//
// WorkRoot ─┬─ Toiling [Initial]
//           └─ Toiled [Final]

public struct InvRoot : IRootState
{
}

[Initial]
public struct InvIdle : IState<InvRoot>
{
}

public struct InvRunning : IState<InvRoot>
{
}

public struct InvParallel : IState<InvRoot>
{
}

public struct InvJoined : IState<InvRoot>
{
}

/// <summary>Known to the parent and the worker.</summary>
public struct Shout : IEvent
{
}

/// <summary>Known to the worker alone.</summary>
public struct Whisper : IEvent
{
}

/// <summary>A parent that runs workers: one forwarded to, and two side by side.</summary>
[Module]
public static class InvokeModule
{
    [Transition(From = typeof(InvIdle), To = typeof(InvRunning)), On(1)]
    public static void Run(in InvIdle from)
    {
    }

    [Transition(From = typeof(InvIdle), To = typeof(InvParallel)), On(2)]
    public static void Split(in InvIdle from)
    {
    }

    [Transition(From = typeof(InvRoot), To = typeof(InvIdle)), On(9)]
    public static void Reset(RecordingContext context) => context.Record("reset");

    [Transition(From = typeof(InvIdle)), OnEvent(typeof(Shout))]
    public static void Heard(RecordingContext context) => context.Record("parent heard");

    [Transition(From = typeof(InvRunning), To = typeof(InvJoined)), OnDone]
    public static class RunningDone
    {
        public static void Completed(RecordingContext context) => context.Record("running done");
    }

    [Transition(From = typeof(InvParallel), To = typeof(InvJoined)), OnDone]
    public static class ParallelDone
    {
        public static void Completed(RecordingContext context) => context.Record("parallel done");
    }

    [Invoke(typeof(InvRunning), Forward = true)]
    public static IStatechart<byte> Worker(RecordingContext context) => context.Spawn();

    [Invoke(typeof(InvParallel))]
    public static IStatechart<byte> Left(RecordingContext context) => context.Spawn();

    [Invoke(typeof(InvParallel))]
    public static IStatechart<byte> Right(RecordingContext context) => context.Spawn();

    [Entered(typeof(InvRunning))]
    public static void RunningEntered(RecordingContext context) => context.Record("entered running");

    [Exited(typeof(InvRunning))]
    public static void RunningExited(RecordingContext context) => context.Record("exited running");
}

public struct WorkRoot : IRootState
{
}

[Initial]
public struct Toiling : IState<WorkRoot>
{
}

[Final]
public struct Toiled : IState<WorkRoot>
{
}

/// <summary>The worker a parent invokes: 1 finishes it, which completes it.</summary>
[Module]
public static class WorkerModule
{
    [Transition(From = typeof(Toiling), To = typeof(Toiled)), On(1)]
    public static void Finish(RecordingContext context) => context.Record("worker done");

    [Transition(From = typeof(Toiling)), OnEvent(typeof(Shout))]
    public static void Heard(RecordingContext context) => context.Record("worker heard");

    [Transition(From = typeof(Toiling)), OnEvent(typeof(Whisper))]
    public static void Whispered(RecordingContext context) => context.Record("worker whispered");

    [Entered(typeof(WorkRoot))]
    public static void Started(RecordingContext context) => context.Record("worker started");

    [Exited(typeof(WorkRoot))]
    public static void Stopped(RecordingContext context) => context.Record("worker stopped");
}
