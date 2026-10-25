namespace StateAlchemist.Contracts.Machines.Permits;

// PermitRoot ─┬─ Open [Initial]
//             └─ Shut ── Latched [Initial]

public struct PermitRoot : IRootState
{
}

[Initial]
public struct Open : IState<PermitRoot>
{
}

public struct Shut : IState<PermitRoot>
{
}

[Initial]
public struct Latched : IState<Shut>
{
}

public struct Knock : IEvent
{
    public bool Loud;
}

public struct Poke : IEvent
{
}

public struct Wave : IEvent
{
}

/// <summary>Which triggers a state would accept: unguarded, guarded by the context, guarded by the value, guarded by the event.</summary>
[Module]
public static class PermitModule
{
    [Transition(From = typeof(Open), To = typeof(Shut)), On(1)]
    public static void Close(in Open from)
    {
    }

    [Transition(From = typeof(Open)), OnRange(5, 9)]
    public static class Ranged
    {
        public static bool Guard(RecordingContext context) => context.Allow.Contains("Ranged");

        public static void Transform(ref Open self)
        {
        }
    }

    [Transition(From = typeof(Open)), OnRange(20, 25)]
    public static class Even
    {
        public static bool Guard(byte value) => value % 2 == 0;

        public static void Transform(ref Open self)
        {
        }
    }

    [Transition(From = typeof(Open)), OnEvent(typeof(Knock))]
    public static class Answer
    {
        public static bool Guard(in Knock knock) => knock.Loud;

        public static void Transform(ref Open self)
        {
        }
    }

    [Transition(From = typeof(Open)), OnEvent(typeof(Poke))]
    public static class Poked
    {
        public static bool Guard(RecordingContext context) => context.Allow.Contains("Poked");

        public static void Transform(ref Open self)
        {
        }
    }

    [Transition(From = typeof(PermitRoot)), OnEvent(typeof(Wave))]
    public static void Waved(ref PermitRoot root)
    {
    }

    [Transition(From = typeof(PermitRoot)), On(100)]
    public static void Hundred(ref PermitRoot root)
    {
    }

    [Transition(From = typeof(PermitRoot)), On(101)]
    public static void HundredAndOne(ref PermitRoot root)
    {
    }

    [Transition(From = typeof(Shut), To = typeof(Open)), On(2)]
    public static void Reopen(in Shut from)
    {
    }

    [Entered(typeof(Latched))]
    public static void Latch(RecordingContext context) => context.Record("entered Latched");

    [Entered(typeof(Shut))]
    public static void ShutEntered(RecordingContext context) => context.Record("entered Shut");

    [Entered(typeof(PermitRoot))]
    public static void RootEntered(RecordingContext context) => context.Record("entered PermitRoot");
}
