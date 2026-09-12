using StateAlchemist.ExportingLibrary;

namespace StateAlchemist.Generated.Tests;

// The case [IncludeExported] exists for: this machine names no module at all. Its transitions come from a
// referenced library that offers one, which is what "add a package, get a protocol" has to mean.
[Machine(Root = typeof(PingRoot), Value = typeof(byte))]
[IncludeExported]
public sealed partial class ExportedMachine;
