namespace StateAlchemist.Samples.Telnet;

// begin-snippet: sample-machine
[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext))]
[Include(typeof(TelnetCore)), Include(typeof(GmcpModule)), Include(typeof(NawsModule))]
public sealed partial class SampleTelnet
{
}
// end-snippet
