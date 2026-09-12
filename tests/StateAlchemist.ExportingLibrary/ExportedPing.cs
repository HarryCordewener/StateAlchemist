using System;

namespace StateAlchemist.ExportingLibrary;

// What a protocol package looks like from the outside: states and a module. The one line that puts the module on
// offer is in AssemblyInfo.cs, where an assembly attribute has to be.

/// <summary>The root of the shape this library extends.</summary>
public struct PingRoot : IRootState
{
    /// <summary>How many pings have been answered.</summary>
    public int Pings;
}

/// <summary>Where the machine rests.</summary>
[Initial]
public struct PingIdle : IState<PingRoot>
{
}

/// <summary>A module offered to any machine that takes what its references export.</summary>
[Module]
public static class ExportedPingModule
{
    /// <summary>9: answer a ping without leaving the state.</summary>
    [Transition(From = typeof(PingIdle)), On(9)]
    public static void Ping(ref PingRoot root) => root.Pings++;
}
