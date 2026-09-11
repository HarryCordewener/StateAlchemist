namespace StateAlchemist.Samples.Telnet;

// begin-snippet: sample-states
/// <summary>The root: lives as long as the connection.</summary>
public struct Connected : IRootState
{
    public bool GmcpEnabled;
    public int Width;
    public int Height;
}

/// <summary>Reading ordinary text. Where the machine starts.</summary>
[Initial]
public struct Idle : IState<Connected>
{
    public int LineLength;
}

/// <summary>After IAC: a command follows.</summary>
public struct Command : IState<Connected>
{
}

[Initial]
public struct AwaitingVerb : IState<Command>
{
}

/// <summary>After IAC WILL: the option follows.</summary>
public struct Willing : IState<Command>
{
}

/// <summary>After IAC SB: a subnegotiation follows.</summary>
public struct SubNegotiation : IState<Connected>
{
    public byte Option;
}

[Initial]
public struct AwaitingOption : IState<SubNegotiation>
{
}

// begin-snippet: sample-naws-state
/// <summary>Collecting NAWS's four bytes.</summary>
public struct Naws : IState<SubNegotiation>
{
    public byte[]? Bytes;
    public int Index;

    /// <summary>Rewinds but keeps the buffer, so entering NAWS again allocates nothing.</summary>
    public void Reset() => Index = 0;
}
// end-snippet

/// <summary>After IAC inside NAWS: SE ends it.</summary>
public struct NawsEscaping : IState<SubNegotiation>
{
    public Naws Captured;
}
// end-snippet
