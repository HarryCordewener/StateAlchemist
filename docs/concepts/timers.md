# Timers

A timer fires a transition when its source state has been active for a set time: a login prompt that gives up
after 30 seconds, a session that closes when the player has been idle too long. The machine owns the timer, so
nothing outside it has to start, cancel or track one.

<!-- snippet: sample-login-timers -->
<a id='snippet-sample-login-timers'></a>
```cs
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
```
<sup><a href='/samples/StateAlchemist.Samples/Login/Login.cs#L33-L57' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-login-timers' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`[After]` is a trigger, used in place of `[On]`, `[OnRange]`, `[OnAny]` or `[OnEvent]`, and not alongside them
([`SALCH0106`](../reference/diagnostics.md#salch0106)). It takes one of:

- `Milliseconds` or `Seconds`, a positive constant. Set one, not both.
- Neither, with a `Delay` method in the class form, for a delay that is only known at runtime. It returns a
  `TimeSpan`, and may take the source state and its ancestors by `in`, the configuration and the
  context (not under `Purity.Strict`). A delay below zero counts as zero.

A decision cannot fire on `[After]`. A timer's transition can have a `Guard`, `Transform` and `Completed` like any
other, and transitions with the same source and the same `[After]` share one timer: their guards are tried by
`Order`, and the unguarded one, if any, last.

## When a timer runs

- **Started** when its source state is entered: by the transition that enters it, as the state changes, or by
  `StartAsync` for the states on the initial path. A `Delay` method runs then, after that transition's transform,
  so it reads the state's new data. An exception from it is the transform's: the transition does not happen
  ([exceptions](exceptions.md)).
- **Cancelled** when its source state is exited. Moving between states below the source does not touch it. A
  [re-entry](transitions.md) exits and enters, so it starts the timer again; a stay does not.
- **Cancelled** by `StopAsync` and `DisposeAsync`.
- **Fired** once. Its transition is tried from the active state, which is the source or a state below it. It is
  not looked for up the tree, and never reaches `OnUnhandled`. If every guard refuses, nothing happens, and the
  timer does not start again until the state is entered again.

A firing that arrives after its state was left is dropped, even when the state has been entered again since: that
entry started a new timer, and only it can fire.

## The clock

A machine with a timer takes an optional `TimeProvider` as its last constructor parameter, and uses
`TimeProvider.System` without one. A test passes a `FakeTimeProvider`, from the
`Microsoft.Extensions.TimeProvider.Testing` package, and moves time itself:

<!-- snippet: sample-login-clock -->
<a id='snippet-sample-login-clock'></a>
```cs
var clock = new FakeTimeProvider();
var context = new SessionContext { IdleLimit = TimeSpan.FromMinutes(5) };
await using var login = new LoginMachine(context, clock);
await login.StartAsync();                          // Prompting's 30-second timer starts
await login.FireAsync((byte)1);                    // logged in: that timer is cancelled, Playing's starts

clock.Advance(TimeSpan.FromMinutes(4));
await login.FireAsync((byte)'x');                  // input re-enters Playing: five minutes from now
clock.Advance(TimeSpan.FromMinutes(5));            // the timer fires while Advance runs
// login.IsIn<Disconnected>() is now true, and context.Log holds "idled".
```
<sup><a href='/tests/StateAlchemist.Generated.Tests/DocumentationExampleTests.cs#L57-L68' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-login-clock' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`TimeProvider` is part of .NET 8 and later. On `netstandard2.0` the package brings in `Microsoft.Bcl.TimeProvider`,
which declares the same type. A machine without timers has the constructor it always had.

Each timer is one `ITimer` per machine, created the first time its state is entered and reset with `Change` after
that, so entering a timed state allocates nothing once the timer exists.

## Firing through the inbox

A timer has no caller: its callback runs on a thread the `TimeProvider` chooses. The firing therefore goes through
the machine's [inbox](concurrency.md#serialized), as an input from inside the machine, and a machine with a timer
has an inbox in every concurrency mode. If the machine is idle, the timer's thread runs the transition; if a
caller's transition is running, the firing waits its turn behind it.

The firing never holds a `Checked` machine's one-caller claim, so a caller is never refused with
`ConcurrentUseException` because a timer fired, and in a bounded inbox it takes no room. While a
[decision](decisions.md) is pending, a timer on an active state is handled at once, as if the decision listed it
in `Handle`. A timer that leaves the pending state cancels the decision: the "give up waiting" case, without the
host owning the timer.

## When a timer's transition throws

The phase hooks apply first, as they do to any transition ([exceptions](exceptions.md)). What they leave to
rethrow has no caller to reach, so it goes to a generated hook instead, with the name of the transition that threw:

<!-- not compiled: a hook's declaration, as the generator writes it -->
```csharp
partial void OnTimerException(Exception exception, string transition);
```

The machine keeps running, in whatever state the exception left it. Without the hook, the exception is dropped.

## In the definition and diagrams

A timer's `TriggerDefinition` has `Kind` `TriggerKind.Timer` and `Delay`, the constant delay, or `null` when a
`Delay` method sets it. Its `ToString()`, which labels the arrow in `Mermaid` and `Dot`, is `after 30s`,
`after 250ms` or `after Delay`. There is no `Plan` for a timer: nothing outside the machine can fire one.
