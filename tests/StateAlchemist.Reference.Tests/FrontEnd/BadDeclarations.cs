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
