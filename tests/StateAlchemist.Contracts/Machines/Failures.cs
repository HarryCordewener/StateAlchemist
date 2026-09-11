namespace StateAlchemist.Contracts.Machines.Failures;

// FailRoot ─┬─ Calm [Initial]
//           └─ Moved

public struct FailRoot : IRootState
{
    public int Value;
}

[Initial]
public struct Calm : IState<FailRoot>
{
    public int Value;
}

public struct Moved : IState<FailRoot>
{
}

public readonly struct Recover : IEvent
{
}

/// <summary>Every phase records, so a test can make any one of them throw.</summary>
[Module]
public static class FailureModule
{
    [Transition(From = typeof(Calm), To = typeof(Moved)), On(1)]
    public static class Go
    {
        public static bool Guard(RecordingContext context)
        {
            context.Record("guard Go");
            return true;
        }

        public static void Transform(RecordingContext context) => context.Record("transform Go");

        public static void Completed(RecordingContext context) => context.Record("completed Go");
    }

    [Transition(From = typeof(FailRoot), To = typeof(Calm)), OnEvent(typeof(Recover))]
    public static class Recovered
    {
        public static void Completed(RecordingContext context) => context.Record("recovered");
    }

    [Exited(typeof(Calm))]
    public static void LeaveCalm(RecordingContext context) => context.Record("exited Calm");

    [Entered(typeof(Moved))]
    public static void ArriveMoved(RecordingContext context) => context.Record("entered Moved");

    [Entered(typeof(Moved))]
    public static void ArriveMovedAgain(RecordingContext context) => context.Record("entered Moved again");
}
