namespace StateAlchemist.Contracts.Machines.Timing;

// TimeRoot ─┬─ Idle [Initial]
//           ├─ Recording ─┬─ Rolling [Initial]      after 60s → TimedOut
//           │             └─ Paused
//           ├─ Waiting                               after Delay (its Seconds) → Idle
//           ├─ Choosing                              after 10s: Left if allowed, else Right; after 20s: only if allowed
//           ├─ Exploding                             after 1s, whose Transform throws
//           ├─ Asking                                after 30s → TimedOut, while a decision may be pending
//           ├─ TimedOut
//           ├─ Left
//           └─ Right

public struct TimeRoot : IRootState
{
    public int Fired;
}

[Initial]
public struct Idle : IState<TimeRoot>;

public struct Recording : IState<TimeRoot>
{
    public int Takes;
}

[Initial]
public struct Rolling : IState<Recording>;

public struct Paused : IState<Recording>;

public struct Waiting : IState<TimeRoot>
{
    public int Seconds;
}

public struct Choosing : IState<TimeRoot>;

public struct Exploding : IState<TimeRoot>;

public struct Asking : IState<TimeRoot>;

public struct TimedOut : IState<TimeRoot>;

public struct Left : IState<TimeRoot>;

public struct Right : IState<TimeRoot>;

public readonly record struct Yes(string Who);

public readonly record struct No(int Code);

public union Answer(Yes, No);
