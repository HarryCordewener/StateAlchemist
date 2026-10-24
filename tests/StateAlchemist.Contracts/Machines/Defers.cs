namespace StateAlchemist.Contracts.Machines.Defers;

// DefRoot ─┬─ Busy [Initial] [Defer(Nudge)] ── Inner [Initial]
//          └─ Free

public struct DefRoot : IRootState
{
}

[Initial, Defer(typeof(Nudge))]
public struct Busy : IState<DefRoot>
{
}

[Initial]
public struct Inner : IState<Busy>
{
}

public struct Free : IState<DefRoot>
{
}

public struct Nudge : IEvent
{
    public int N;
}

/// <summary>Raised only from inside the machine.</summary>
[Internal]
public struct Tick : IEvent
{
}

/// <summary>An event deferred while busy, delivered once the machine moves.</summary>
[Module]
public static class DeferModule
{
    [Transition(From = typeof(Busy), To = typeof(Free)), On(1)]
    public static void Finish(in Busy from)
    {
    }

    [Transition(From = typeof(Free), To = typeof(Busy)), On(2)]
    public static void Back(in Free from)
    {
    }

    [Transition(From = typeof(Busy)), On(3)]
    public static void Fidget(ref Busy self)
    {
    }

    [Transition(From = typeof(Free)), On(5)]
    public static class Raise
    {
        public static void Completed(RecordingContext context) => context.Machine!.Enqueue(new Tick());
    }

    [Transition(From = typeof(Free)), OnEvent(typeof(Tick))]
    public static void Ticked(RecordingContext context) => context.Record("tick");

    [Transition(From = typeof(Free)), OnEvent(typeof(Nudge))]
    public static void Nudged(in Nudge ping, RecordingContext context) => context.Record("nudge " + ping.N);

    [Transition(From = typeof(Inner)), OnEvent(typeof(Nudge))]
    public static class InnerNudge
    {
        public static bool Guard(RecordingContext context) => context.Allow.Contains("Inner");

        public static void Transform(in Nudge ping, RecordingContext context) => context.Record("inner nudge " + ping.N);
    }
}
