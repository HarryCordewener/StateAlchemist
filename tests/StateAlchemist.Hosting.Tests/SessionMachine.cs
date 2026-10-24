using System.Collections.Generic;

namespace StateAlchemist.Hosting.Tests;

/// <summary>A session per key: 1 closes it, which completes it.</summary>
public struct SessionRoot : IRootState
{
}

[Initial]
public struct SessionOpen : IState<SessionRoot>
{
    public int Hits;
}

[Final]
public struct SessionClosed : IState<SessionRoot>
{
}

[Module]
public static class SessionModule
{
    [Transition(From = typeof(SessionOpen), To = typeof(SessionClosed)), On(1)]
    public static void Close(in SessionOpen from)
    {
    }

    [Transition(From = typeof(SessionOpen)), On(2)]
    public static void Hit(ref SessionOpen self) => self.Hits++;

    [Transition(From = typeof(SessionRoot)), OnAny]
    public static void Ignore()
    {
    }

    [Entered(typeof(SessionRoot))]
    public static void Opened(List<string> log) => log.Add("opened");

    [Exited(typeof(SessionRoot))]
    public static void Ended(List<string> log) => log.Add("ended");
}

[Machine(Root = typeof(SessionRoot), Value = typeof(byte), Context = typeof(List<string>))]
[Include(typeof(SessionModule))]
public sealed partial class Session;
