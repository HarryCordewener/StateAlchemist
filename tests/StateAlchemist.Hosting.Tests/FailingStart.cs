using System;

namespace StateAlchemist.Hosting.Tests;

/// <summary>A machine whose initial state's <c>[Entered]</c> action throws, so starting it fails.</summary>
public struct FailingRoot : IRootState
{
}

/// <summary>Where it starts.</summary>
[Initial]
public struct FailingIdle : IState<FailingRoot>
{
}

[Module]
public static class FailingModule
{
    [Entered(typeof(FailingIdle))]
    public static void Refuse() => throw new InvalidOperationException("cannot start");

    [Transition(From = typeof(FailingRoot)), OnAny]
    public static void Ignore()
    {
    }
}

[Machine(Root = typeof(FailingRoot), Value = typeof(byte))]
[Include(typeof(FailingModule))]
public sealed partial class FailingStart;
