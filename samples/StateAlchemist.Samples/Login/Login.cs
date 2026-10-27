using System;
using System.Collections.Generic;

namespace StateAlchemist.Samples.Login;

// A login prompt with two timers: a fixed one on the prompt, and one on play whose length is read at runtime.
//
// Session ─┬─ Prompting [Initial]   after 30s → Disconnected
//          ├─ Playing               after the context's IdleLimit → Disconnected; any input restarts it
//          └─ Disconnected

public struct Session : IRootState;

[Initial]
public struct Prompting : IState<Session>;

public struct Playing : IState<Session>;

public struct Disconnected : IState<Session>;

public sealed class SessionContext
{
    /// <summary>How long a player may send nothing before the session closes.</summary>
    public TimeSpan IdleLimit { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Why the session closed.</summary>
    public List<string> Log { get; } = [];
}

[Module]
public static class SessionModule
{
    // begin-snippet: sample-login-timers
    /// <summary>A visitor has 30 seconds to log in. Leaving Prompting cancels the timer.</summary>
    [Transition(From = typeof(Prompting), To = typeof(Disconnected)), After(Seconds = 30)]
    public static void TooSlow(SessionContext context) => context.Log.Add("too slow");

    [Transition(From = typeof(Prompting), To = typeof(Playing)), On(1)]
    public static void LoggedIn()
    {
    }

    /// <summary>Any input re-enters Playing, and a re-entry starts its timer again.</summary>
    [Transition(From = typeof(Playing), To = typeof(Playing)), OnAny]
    public static void Active()
    {
    }

    /// <summary>The idle limit is not a constant: a Delay method reads it each time Playing is entered.</summary>
    [Transition(From = typeof(Playing), To = typeof(Disconnected)), After]
    public static class Idled
    {
        public static TimeSpan Delay(SessionContext context) => context.IdleLimit;

        public static void Completed(SessionContext context) => context.Log.Add("idled");
    }
    // end-snippet

    /// <summary>Before login and after closing, input is ignored.</summary>
    [Transition(From = typeof(Session)), OnAny]
    public static void Ignore()
    {
    }
}

[Machine(Root = typeof(Session), Value = typeof(byte), Context = typeof(SessionContext))]
[Include(typeof(SessionModule))]
public sealed partial class LoginMachine;
