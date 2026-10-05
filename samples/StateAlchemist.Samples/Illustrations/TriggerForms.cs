using StateAlchemist.Samples.Telnet;
using static StateAlchemist.Samples.Telnet.TelnetBytes;

namespace StateAlchemist.Samples.Illustrations;

// Modules the concept pages quote. No machine includes them — each would collide with the telnet core, which
// handles the same states and bytes — but they are compiled, and the analyzer checks them where they are written.

/// <summary>Each way to say what fires a transition.</summary>
[Module]
public static class TriggerForms
{
    // begin-snippet: sample-trigger-forms
    [Transition(From = typeof(Idle)), On(LineFeed)]              // one value
    public static void EndOfLine(ref Idle self) => self.LineLength = 0;

    [Transition(From = typeof(Idle)), OnRange(0x20, 0x7E)]       // a range, inclusive
    public static void Printable(ref Idle self, byte value) => self.LineLength++;

    [Transition(From = typeof(Idle)), OnAny]                     // anything else
    public static void Text(ref Idle self, byte value) => self.LineLength++;

    [Transition(From = typeof(Connected), To = typeof(Idle)), OnEvent(typeof(Error))]
    public static void Recover(in Error error) { }
    // end-snippet
}

/// <summary>A guarded transition and its unguarded fallback.</summary>
[Module]
public static class GuardedIac
{
    // begin-snippet: sample-guard-fallback
    [Transition(From = typeof(Idle), To = typeof(Command), Order = 1), On(Iac)]
    public static class CommandWhenNegotiating
    {
        public static bool Guard(TelnetContext context) => context.Negotiating;
    }

    [Transition(From = typeof(Idle)), On(Iac)]                    // unguarded: the fallback
    public static void LiteralIac(ref Idle self) => self.LineLength++;
    // end-snippet
}

/// <summary>A second module's action on a state another module owns.</summary>
[Module]
public static class NawsTrace
{
    // begin-snippet: sample-action-order
    [Exited(typeof(Naws), Order = 1)]
    public static void Trace(TelnetContext context, Naws naws) => context.Log.Add($"left NAWS at {naws.Index}");
    // end-snippet
}
