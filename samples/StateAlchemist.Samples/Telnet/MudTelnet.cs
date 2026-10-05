namespace StateAlchemist.Samples.Telnet;

// begin-snippet: sample-mud-telnet
[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext))]
[Include(typeof(TelnetCore)), Include(typeof(NawsModule))]
public sealed partial class MudTelnet;
// end-snippet
