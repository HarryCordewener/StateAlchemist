using System.Threading.Tasks;
using static StateAlchemist.Samples.Telnet.TelnetBytes;

namespace StateAlchemist.Samples.Telnet;

/// <summary>Accepts GMCP: a plugin adding a transition out of a state it does not own.</summary>
// begin-snippet: sample-gmcp-module
[Module]
public static class GmcpModule
{
    // begin-snippet: sample-class-form
    [Transition(From = typeof(Willing), To = typeof(Idle)), On(GmcpOption)]
    public static class Accept
    {
        public static void Transform(ref Connected root) => root.GmcpEnabled = true;

        public static ValueTask CompletedAsync(TelnetContext context) => context.SendAsync(Iac, Do, GmcpOption);
    }
    // end-snippet
}
// end-snippet
