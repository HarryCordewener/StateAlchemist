using static StateAlchemist.Samples.Telnet.TelnetBytes;

namespace StateAlchemist.Samples.Telnet;

/// <summary>Reads a NAWS subnegotiation (RFC 1073) into the root's window size.</summary>
// begin-snippet: sample-naws-module
[Module]
public static class NawsModule
{
    // begin-snippet: sample-roles
    [Transition(From = typeof(AwaitingOption), To = typeof(Naws)), On(NawsOption)]
    public static void Begin(in AwaitingOption from, ref SubNegotiation parent, ref Naws to) => parent.Option = NawsOption;
    // end-snippet

    // begin-snippet: sample-capture
    [Transition(From = typeof(Naws)), OnAny]
    public static void Capture(ref Naws self, byte value)
    {
        self.Bytes ??= new byte[4];
        if (self.Index < 4)
        {
            self.Bytes[self.Index] = value;
            self.Index++;
        }
    }
    // end-snippet

    [Transition(From = typeof(Naws), To = typeof(NawsEscaping)), On(Iac)]
    public static void Escape(in Naws from, ref NawsEscaping to) => to.Captured = from;

    // begin-snippet: sample-guard
    [Transition(From = typeof(NawsEscaping), To = typeof(Idle)), On(Se)]
    public static class Finish
    {
        public static bool Guard(in NawsEscaping from) => from.Captured.Index == 4;

        public static void Transform(in NawsEscaping from, ref Connected root)
        {
            var bytes = from.Captured.Bytes!;
            root.Width = (bytes[0] << 8) | bytes[1];
            root.Height = (bytes[2] << 8) | bytes[3];
        }

        public static void Completed(TelnetContext context, Connected root) => context.Log.Add($"window {root.Width}x{root.Height}");
    }
    // end-snippet
}
// end-snippet
