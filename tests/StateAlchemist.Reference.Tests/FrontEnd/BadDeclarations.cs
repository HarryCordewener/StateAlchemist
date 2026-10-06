using System.Threading.Tasks;
using StateAlchemist.Samples.Telnet;

namespace StateAlchemist.Reference.Tests.FrontEnd;

// Declarations that are wrong on purpose. Each is included on its own, alongside the sample's core module.

[Module]
public static class HiddenModule
{
    [Transition(From = typeof(Idle)), On(1)]
    internal static void Hidden(ref Idle self)
    {
    }
}

[Module]
public static class ExitingWriteModule
{
    [Transition(From = typeof(Naws), To = typeof(Idle)), On(1)]
    public static void Leave(ref SubNegotiation parent)
    {
    }
}

[Module]
public static class MisnamedModule
{
    [Transition(From = typeof(Willing), To = typeof(Idle)), On(2)]
    public static class Refuse
    {
        public static void Transfrom(ref Connected root)
        {
        }

        public static ValueTask Completed(TelnetContext context) => default;
    }
}

[Module]
public static class NoTriggerModule
{
    [Transition(From = typeof(Idle))]
    public static void Nothing(ref Idle self)
    {
    }
}

[Module]
public static class RivalGmcpModule
{
    [Transition(From = typeof(Willing), To = typeof(Idle)), On(TelnetBytes.GmcpOption)]
    public static void AcceptToo(in Willing from)
    {
    }
}

public static class NotAModule
{
}

[Module]
public static class TimerBothUnitsModule
{
    [Transition(From = typeof(Idle)), After(Milliseconds = 500, Seconds = 1)]
    public static void Both()
    {
    }
}

[Module]
public static class TimerNoDelayModule
{
    [Transition(From = typeof(Idle)), After]
    public static void Never()
    {
    }
}

[Module]
public static class TimerTwoDelaysModule
{
    [Transition(From = typeof(Idle)), After(Seconds = 1)]
    public static class Twice
    {
        public static System.TimeSpan Delay() => System.TimeSpan.FromSeconds(2);
    }
}

[Module]
public static class TimerMixedModule
{
    [Transition(From = typeof(Idle)), After(Seconds = 1), On(1)]
    public static void Mixed()
    {
    }
}

[Module]
public static class TimerDecisionModule
{
    [Decision(From = typeof(Idle)), After(Seconds = 1)]
    public static class Wait
    {
        public static ValueTask<Willing> DecideAsync(System.Threading.CancellationToken cancellation) => default;

        [To(typeof(Idle))]
        public static void Complete(Willing outcome)
        {
        }
    }
}

[Module]
public static class StrayDelayModule
{
    [Transition(From = typeof(Idle)), On(1)]
    public static class Stray
    {
        public static System.TimeSpan Delay() => System.TimeSpan.FromSeconds(1);
    }
}

[Module]
public static class DelayReadsLeftStateModule
{
    [Transition(From = typeof(Idle)), After]
    public static class Late
    {
        public static System.TimeSpan Delay(in Naws other, ref Idle self) => System.TimeSpan.FromSeconds(1);
    }
}

[Module]
public static class TimerNegativeModule
{
    [Transition(From = typeof(Idle)), After(Seconds = -1)]
    public static void Back()
    {
    }
}

[Module]
public static class TimerTooLongModule
{
    [Transition(From = typeof(Idle)), After(Seconds = 5000000)]
    public static void Forever()
    {
    }
}
