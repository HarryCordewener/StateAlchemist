using System.Threading.Tasks;
using static StateAlchemist.Samples.Telnet.TelnetBytes;

namespace StateAlchemist.Samples.Telnet;

/// <summary>Reading text, recognising IAC, and refusing any option no other module accepts.</summary>
// begin-snippet: sample-telnet-core
[Module]
public static class TelnetCore
{
    // begin-snippet: sample-stay
    /// <summary>Ordinary text: stay in Idle and count the line.</summary>
    [Transition(From = typeof(Idle)), OnAny]
    public static void Text(ref Idle self, byte value) => self.LineLength++;
    // end-snippet

    [Transition(From = typeof(Idle)), On(LineFeed)]
    public static void EndOfLine(ref Idle self) => self.LineLength = 0;

    // begin-snippet: sample-move
    [Transition(From = typeof(Idle), To = typeof(Command)), On(Iac)]
    public static void BeginCommand(in Idle from)
    {
    }
    // end-snippet

    [Transition(From = typeof(AwaitingVerb), To = typeof(Willing)), On(Will)]
    public static void BeginWilling(in AwaitingVerb from, ref Willing to)
    {
    }

    [Transition(From = typeof(AwaitingVerb), To = typeof(SubNegotiation)), On(Sb)]
    public static void BeginSubNegotiation(in AwaitingVerb from, ref SubNegotiation to)
    {
    }

    [Transition(From = typeof(AwaitingVerb), To = typeof(Idle)), OnAny]
    public static void UnknownCommand(in AwaitingVerb from)
    {
    }

    /// <summary>A subnegotiation no module understands is abandoned.</summary>
    [Transition(From = typeof(SubNegotiation), To = typeof(Idle)), OnAny]
    public static void Abandon(in SubNegotiation from)
    {
    }

    // begin-snippet: sample-or-else
    /// <summary>Any option no module accepts is refused.</summary>
    [Transition(From = typeof(Willing), To = typeof(Idle)), OnAny]
    public static class Refuse
    {
        public static ValueTask CompletedAsync(TelnetContext context, byte option) => context.SendAsync(Iac, Dont, option);
    }
    // end-snippet

    // begin-snippet: sample-event-transition
    /// <summary>Declared on the root, so it applies in every state.</summary>
    [Transition(From = typeof(Connected), To = typeof(Idle)), OnEvent(typeof(Error))]
    public static void Recover(in Error error)
    {
    }
    // end-snippet

    // begin-snippet: sample-entered
    [Entered(typeof(Idle))]
    public static void Ready(TelnetContext context) => context.Log.Add("ready");
    // end-snippet
}
// end-snippet
