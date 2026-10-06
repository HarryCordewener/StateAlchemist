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

public readonly struct Knock : IEvent
{
}

public readonly struct Ring : IEvent
{
}

[Module]
public static class GuardedJoinModule
{
    [Transition(From = typeof(Idle), To = typeof(Command)), OnAll(typeof(Knock), typeof(Ring))]
    public static class Answer
    {
        public static bool Guard(TelnetContext context) => true;
    }
}

[Module]
public static class MixedJoinModule
{
    [Transition(From = typeof(Idle), To = typeof(Command)), OnEvent(typeof(Error)), OnAll(typeof(Knock), typeof(Ring))]
    public static void Answer()
    {
    }
}

[Module]
public static class LonelyJoinModule
{
    [Transition(From = typeof(Idle), To = typeof(Command)), OnAll(typeof(Knock))]
    public static void Answer()
    {
    }
}

[Module]
public static class UnlistedJoinEventModule
{
    [Transition(From = typeof(Idle), To = typeof(Command)), OnAll(typeof(Knock), typeof(Ring))]
    public static void Answer(in Error error)
    {
    }
}
