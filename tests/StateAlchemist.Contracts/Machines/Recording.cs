namespace StateAlchemist.Contracts.Machines.Recording;

// Root ─┬─ A [Initial] ─┬─ A1 [Initial]
//       │               └─ A2
//       └─ B ────────────── B1 [Initial]

public struct Root : IRootState
{
    public int Counter;
}

[Initial]
public struct A : IState<Root>
{
    public int Value;
}

[Initial]
public struct A1 : IState<A>
{
    public int Value;
}

public struct A2 : IState<A>
{
    public int Value;
}

public struct B : IState<Root>
{
    public int Value;
}

[Initial]
public struct B1 : IState<B>
{
    public int[]? Buffer;
    public int Count;

    public void Reset() => Count = 0;
}

public readonly struct Ping : IEvent
{
    public int Amount { get; init; }
}
