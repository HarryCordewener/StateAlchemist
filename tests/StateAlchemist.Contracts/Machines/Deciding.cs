namespace StateAlchemist.Contracts.Machines.Deciding;

// DecideRoot ─┬─ Asking [Initial]
//             ├─ Account
//             └─ Refused

public struct DecideRoot : IRootState
{
    public int Ticks;
}

[Initial]
public struct Asking : IState<DecideRoot>
{
    public int Question;
}

public struct Account : IState<DecideRoot>
{
    public string? Name;
}

public struct Refused : IState<DecideRoot>
{
    public int Code;
}

/// <summary>The connection closed: the one event the pending decision handles at once.</summary>
public readonly struct Hangup : IEvent;

/// <summary>An event the pending decision does not handle: it waits for the decision.</summary>
public readonly struct Nudge : IEvent;

public readonly record struct Accept(string Name);

public readonly record struct Reject(int Code);

public union Verdict(Accept, Reject);
