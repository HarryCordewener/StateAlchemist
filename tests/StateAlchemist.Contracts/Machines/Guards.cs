namespace StateAlchemist.Contracts.Machines.Guards;

// GuardRoot ─┬─ Waiting [Initial]
//            └─ Chosen

public struct GuardRoot : IRootState
{
    public int Hits;
}

[Initial]
public struct Waiting : IState<GuardRoot>
{
}

public struct Chosen : IState<GuardRoot>
{
    public string? By;
}

/// <summary>Resolution order: exact before range before any, guards in Order, the unguarded one last, a child's or-else before its parent.</summary>
[Module]
public static class GuardModule
{
    [Transition(From = typeof(Waiting), To = typeof(Chosen), Order = 1), On(1)]
    public static class First
    {
        public static bool Guard(RecordingContext context) => context.Allow.Contains("First");

        public static void Transform(ref Chosen to) => to.By = "First";
    }

    [Transition(From = typeof(Waiting), To = typeof(Chosen), Order = 2), On(1)]
    public static class Second
    {
        public static bool Guard(RecordingContext context) => context.Allow.Contains("Second");

        public static void Transform(ref Chosen to) => to.By = "Second";
    }

    [Transition(From = typeof(Waiting), To = typeof(Chosen)), On(1)]
    public static void Fallback(ref Chosen to) => to.By = "Fallback";

    [Transition(From = typeof(Waiting), To = typeof(Chosen)), OnRange(10, 19)]
    public static void Range(ref Chosen to) => to.By = "Range";

    [Transition(From = typeof(Waiting), To = typeof(Chosen)), OnAny]
    public static void Anything(ref Chosen to) => to.By = "Any";

    [Transition(From = typeof(GuardRoot)), On(42)]
    public static void RootStay(ref GuardRoot root) => root.Hits++;

    [Transition(From = typeof(Chosen), To = typeof(Waiting)), On(0)]
    public static void Again(in Chosen from)
    {
    }
}
