namespace StateAlchemist.Contracts.Machines.Recalling;

// Jukebox ─┬─ Idle [Initial]
//          └─ Player ─┬─ Stopped [Initial]
//                     └─ Playing ─┬─ Slow [Initial]
//                                 └─ Fast

public struct Jukebox : IRootState
{
    public int Leaves;
}

[Initial]
public struct Idle : IState<Jukebox>
{
}

public struct Player : IState<Jukebox>
{
    public int Resumes;
}

[Initial]
public struct Stopped : IState<Player>
{
}

public struct Playing : IState<Player>
{
}

[Initial]
public struct Slow : IState<Playing>
{
}

public struct Fast : IState<Playing>
{
    public int Count;
}

public readonly record struct Resume;

public readonly record struct Fresh;

public union Choice(Resume, Fresh);

public readonly struct Coin : IEvent
{
}

public readonly struct Pick : IEvent
{
}
