# Examples

Five machines, smallest first. Each is compiled in `samples/StateAlchemist.Samples` and run by a test, so what
these pages show is what the code does.

| | Example | Shows |
|---|---|---|
| 1 | [A phone call](#a-phone-call) | states, triggers, transitions, actions |
| 2 | [A pedestrian crossing](#a-pedestrian-crossing) | hierarchy, data lifetime, guards, a move across two levels |
| 3 | [Telnet](writing-a-module.md) | modules from other libraries, runs, events |
| 4 | [A door with a card reader](#a-door-with-a-card-reader) | async decisions, cancellation, recovery |
| 5 | [Reading a pipe](#reading-a-pipe) | batches, runs, `Serialized`, backpressure |

## A phone call

The same example Stateless opens with. A fixed set of triggers is an enum, and the machine fires that enum:

<!-- snippet: sample-phone-triggers -->
<a id='snippet-sample-phone-triggers'></a>
```cs
/// <summary>
/// The triggers. A machine whose triggers are a fixed set names them in an enum and fires that: the compiler then
/// knows every trigger there is, and can say which states ignore which.
/// </summary>
public enum Button : byte
{
    /// <summary>Lift the handset.</summary>
    Lift = 1,

    /// <summary>The other end answers.</summary>
    Answered,

    /// <summary>Hold, or take off hold.</summary>
    Hold,

    /// <summary>Hang up.</summary>
    HangUp,
}
```
<sup><a href='/samples/StateAlchemist.Samples/Phone/PhoneCall.cs#L15-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-phone-triggers' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

States are structs; the one marked `[Initial]` is where the machine starts:

<!-- snippet: sample-phone-states -->
<a id='snippet-sample-phone-states'></a>
```cs
/// <summary>The phone, and what it remembers for as long as it is switched on.</summary>
public struct Phone : IRootState
{
    /// <summary>How many calls have connected.</summary>
    public int Calls;
}

/// <summary>On the hook. Where the machine starts.</summary>
[Initial]
public struct OnHook : IState<Phone>
{
}

/// <summary>Off the hook, waiting for an answer.</summary>
public struct Ringing : IState<Phone>
{
}

/// <summary>
/// Talking. The seconds belong to this state, so putting the call on hold loses them: hold leaves
/// <c>Talking</c>. Data that has to survive a hold belongs in a state above both, which is what the crossing
/// example shows.
/// </summary>
public struct Talking : IState<Phone>
{
    /// <summary>Seconds this call has lasted.</summary>
    public int Seconds;
}

/// <summary>On hold, still connected.</summary>
public struct OnHold : IState<Phone>
{
}
```
<sup><a href='/samples/StateAlchemist.Samples/Phone/PhoneCall.cs#L36-L70' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-phone-states' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A transition is a method when all it does is change data. Actions are past tense, and run after the state has
changed:

<!-- snippet: sample-phone-module -->
<a id='snippet-sample-phone-module'></a>
```cs
/// <summary>The transitions. A method is the whole transition when all it does is change data.</summary>
[Module]
public static class PhoneModule
{
    [Transition(From = typeof(OnHook), To = typeof(Ringing)), On(Button.Lift)]
    public static void Lift()
    {
    }

    [Transition(From = typeof(Ringing), To = typeof(Talking)), On(Button.Answered)]
    public static void Answer(ref Phone phone, ref Talking call)
    {
        phone.Calls++;
        call.Seconds = 0;
    }

    [Transition(From = typeof(Talking), To = typeof(OnHold)), On(Button.Hold)]
    public static void Hold()
    {
    }

    [Transition(From = typeof(OnHold), To = typeof(Talking)), On(Button.Hold)]
    public static void Resume()
    {
    }

    /// <summary>Hanging up works from anywhere: declared on the root, it applies to every state beneath it.</summary>
    [Transition(From = typeof(Phone), To = typeof(OnHook)), On(Button.HangUp)]
    public static void HangUp()
    {
    }

    /// <summary>A button that does not apply where the phone is. Without this, the compiler asks about it.</summary>
    [Transition(From = typeof(Phone)), OnAny]
    public static void Ignore()
    {
    }

    /// <summary>An action: past tense, so it runs after the state has changed.</summary>
    [Entered(typeof(Talking))]
    public static void Answered(PhoneLog log, Phone phone) => log.Entries.Add($"connected (call {phone.Calls})");

    /// <summary>Reads the state being left, which is still readable here and cleared straight after.</summary>
    [Exited(typeof(Talking))]
    public static void Ended(PhoneLog log, Talking call) => log.Entries.Add($"talked for {call.Seconds}s");
}
```
<sup><a href='/samples/StateAlchemist.Samples/Phone/PhoneCall.cs#L72-L119' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-phone-module' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The application names the root, the trigger type and the modules it wants:

<!-- snippet: sample-phone-machine -->
<a id='snippet-sample-phone-machine'></a>
```cs
/// <summary>The machine: a root, the trigger type, and the modules this application wants.</summary>
[Machine(Root = typeof(Phone), Value = typeof(Button), Context = typeof(PhoneLog))]
[Include(typeof(PhoneModule))]
public sealed partial class PhoneCall;
```
<sup><a href='/samples/StateAlchemist.Samples/Phone/PhoneCall.cs#L121-L126' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-phone-machine' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

```csharp
await using var phone = new PhoneCall(log);
await phone.StartAsync();
await phone.FireAsync(Button.Lift);
await phone.FireAsync(Button.Answered);       // log: "connected (call 1)"
phone.TryGetPhone(out var state);             // state.Calls == 1
```

Holding the call leaves `Talking`, so the seconds it counts start again afterwards. Data that has to survive
belongs in a state above both — which is the next example.

## A pedestrian crossing

Hierarchy, and what it does for data:

<!-- snippet: sample-crossing-triggers -->
<a id='snippet-sample-crossing-triggers'></a>
```cs
/// <summary>What can happen to a crossing.</summary>
public enum Signal : byte
{
    /// <summary>A second of the cycle.</summary>
    Tick = 1,

    /// <summary>Someone pressed the button.</summary>
    Requested,

    /// <summary>The controller reports a fault.</summary>
    Fault,

    /// <summary>An engineer clears it.</summary>
    Cleared,
}
```
<sup><a href='/samples/StateAlchemist.Samples/Crossing/Crossing.cs#L15-L31' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-crossing-triggers' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample-crossing-states -->
<a id='snippet-sample-crossing-states'></a>
```cs
/// <summary>The junction. Its data lives as long as the machine.</summary>
public struct Junction : IRootState
{
    /// <summary>Completed light cycles since the machine started.</summary>
    public int Cycles;
}

/// <summary>Signalling normally. A pending request belongs here: both lights need it, neither owns it.</summary>
[Initial]
public struct Working : IState<Junction>
{
    /// <summary>Whether someone is waiting to cross.</summary>
    public bool Requested;
}

/// <summary>Traffic stopped. Where a working crossing starts.</summary>
[Initial]
public struct Red : IState<Working>
{
}

/// <summary>Traffic moving. What it counts dies when the light changes.</summary>
public struct Green : IState<Working>
{
    /// <summary>Seconds this green has lasted.</summary>
    public int Seconds;
}

/// <summary>About to stop.</summary>
public struct Amber : IState<Working>
{
}

/// <summary>Out of service.</summary>
public struct Faulted : IState<Junction>
{
}

/// <summary>What a faulted crossing does. Every state with children names one to enter first.</summary>
[Initial]
public struct Flashing : IState<Faulted>
{
}
```
<sup><a href='/samples/StateAlchemist.Samples/Crossing/Crossing.cs#L33-L77' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-crossing-states' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`Working` holds the button's request because both lights need it and neither owns it; `Green` holds its own
seconds, which go when the light changes. Two transitions want `Tick` in `Green`, so the guarded one declares an
`Order` and the unguarded one is the fallback:

<!-- snippet: sample-crossing-module -->
<a id='snippet-sample-crossing-module'></a>
```cs
[Module]
public static class CrossingModule
{
    [Transition(From = typeof(Red), To = typeof(Green)), On(Signal.Tick)]
    public static void Go(ref Green green) => green.Seconds = 0;

    /// <summary>
    /// Two transitions want <c>Tick</c> in <see cref="Green"/>. The guarded one is tried first, by
    /// <c>Order</c>; the unguarded one is the fallback.
    /// </summary>
    [Transition(From = typeof(Green), To = typeof(Amber), Order = 1), On(Signal.Tick)]
    public static class Change
    {
        /// <summary>Change early for someone waiting, but not before the green has run for three seconds.</summary>
        public static bool Guard(in Green green, in Working working) => working.Requested && green.Seconds >= 3;

        public static void Transform()
        {
        }
    }

    [Transition(From = typeof(Green)), On(Signal.Tick)]
    public static void Count(ref Green green) => green.Seconds++;

    /// <summary>
    /// Amber to red crosses no boundary the button cares about: <see cref="Working"/> stays, so the request it
    /// holds can be cleared here, and the cycle counted on the root.
    /// </summary>
    [Transition(From = typeof(Amber), To = typeof(Red)), On(Signal.Tick)]
    public static void Stop(ref Working working, ref Junction junction)
    {
        working.Requested = false;
        junction.Cycles++;
    }

    /// <summary>The button, wherever the crossing is working. The flag lives above both lights, so it survives.</summary>
    [Transition(From = typeof(Working)), On(Signal.Requested)]
    public static void Request(ref Working working) => working.Requested = true;

    /// <summary>A fault moves two levels: out of the light and out of <see cref="Working"/>, into the flashing state.</summary>
    [Transition(From = typeof(Working), To = typeof(Faulted)), On(Signal.Fault)]
    public static void Fail()
    {
    }

    /// <summary>And back. Entering <see cref="Working"/> continues to its <c>[Initial]</c> child, so it starts at red.</summary>
    [Transition(From = typeof(Faulted), To = typeof(Working)), On(Signal.Cleared)]
    public static void Clear()
    {
    }

    /// <summary>Anything a state does not handle.</summary>
    [Transition(From = typeof(Junction)), OnAny]
    public static void Ignore()
    {
    }

    [Entered(typeof(Green))]
    public static void Started(CrossingLog log) => log.Entries.Add("green");

    /// <summary>The state being left is still readable here; it is cleared immediately afterwards.</summary>
    [Exited(typeof(Green))]
    public static void Ended(CrossingLog log, Green green) => log.Entries.Add($"green lasted {green.Seconds}s");

    [Entered(typeof(Flashing))]
    public static void Broke(CrossingLog log) => log.Entries.Add("flashing");
}
```
<sup><a href='/samples/StateAlchemist.Samples/Crossing/Crossing.cs#L79-L147' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-crossing-module' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample-crossing-machine -->
<a id='snippet-sample-crossing-machine'></a>
```cs
[Machine(Root = typeof(Junction), Value = typeof(Signal), Context = typeof(CrossingLog))]
[Include(typeof(CrossingModule))]
public sealed partial class PedestrianCrossing;
```
<sup><a href='/samples/StateAlchemist.Samples/Crossing/Crossing.cs#L149-L153' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-crossing-machine' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

A fault moves across two levels — out of the light, out of `Working`, into `Faulted`, and down to its `[Initial]`
child. Clearing it enters `Working` and lands on `Red` the same way.

## A door with a card reader

When something outside the machine decides the outcome, and may take a while, that is a
[decision](../concepts/decisions.md). Its result is a union, and each case gets its own `Complete`:

<!-- snippet: sample-door-outcomes -->
<a id='snippet-sample-door-outcomes'></a>
```cs
/// <summary>What the reader can decide. Each case carries what its outcome needs.</summary>
public readonly record struct Allowed(string Name);

/// <summary>The badge is not allowed in.</summary>
public readonly record struct Refused(string Reason);

/// <summary>The decision's result: one of these, and the generator writes a <c>Complete</c> for each.</summary>
public union Answer(Allowed, Refused);
```
<sup><a href='/samples/StateAlchemist.Samples/Door/Door.cs#L32-L41' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-door-outcomes' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample-door-decision -->
<a id='snippet-sample-door-decision'></a>
```cs
[Module]
public static class DoorModule
{
    /// <summary>
    /// A decision: the machine moves into a pending state below <see cref="Locked"/>, the caller's
    /// <c>FireAsync</c> keeps waiting, and the outcome picks the transition that follows.
    /// </summary>
    [Decision(From = typeof(Locked), Handle = new[] { typeof(GaveUp) }), OnEvent(typeof(Badge))]
    public static class Read
    {
        public static async ValueTask<Answer> DecideAsync(Access access, Badge badge, CancellationToken cancellation) =>
            await access.Check(badge.Number, cancellation).ConfigureAwait(false)
                ? new Answer(new Allowed($"badge {badge.Number}"))
                : new Answer(new Refused("unknown badge"));

        /// <summary>One <c>Complete</c> per outcome, each naming where that outcome goes.</summary>
        [To(typeof(Unlocked))]
        public static void Complete(ref DoorFrame door, ref Unlocked unlocked, Allowed outcome)
        {
            door.Admitted++;
            unlocked.Name = outcome.Name;
        }

        [To(typeof(Locked))]
        public static void Complete(Refused outcome)
        {
        }

        /// <summary>And an action per outcome, after the state has changed.</summary>
        public static void Completed(Access access, Allowed outcome) => access.Log.Add($"opened for {outcome.Name}");

        public static void Completed(Access access, Refused outcome) => access.Log.Add($"refused: {outcome.Reason}");
    }

    /// <summary>
    /// <c>Handle</c> above lets this run while the decision is pending, and what ends the wait is *leaving* the
    /// pending state: this names <c>Locked</c> as its target, so it is a transition out of the pending state and
    /// the reader's <c>CancellationToken</c> is cancelled. A stay would keep waiting.
    /// </summary>
    [Transition(From = typeof(Locked), To = typeof(Locked)), OnEvent(typeof(GaveUp))]
    public static class Abandon
    {
        public static void Transform()
        {
        }

        public static void Completed(Access access) => access.Log.Add("gave up waiting");
    }

    /// <summary>A reader that throws fires <see cref="DecisionFailed"/>, which is an ordinary trigger to recover from.</summary>
    [Transition(From = typeof(Locked)), OnEvent(typeof(DecisionFailed))]
    public static class Broken
    {
        public static void Transform()
        {
        }

        public static void Completed(Access access, DecisionFailed failure) =>
            access.Log.Add($"reader failed: {failure.Exception.Message}");
    }

    [Transition(From = typeof(Unlocked), To = typeof(Locked)), OnEvent(typeof(GaveUp))]
    public static void Close()
    {
    }

    /// <summary>This door is driven by events; a machine still says what its values do, and here they do nothing.</summary>
    [Transition(From = typeof(DoorFrame)), OnAny]
    public static void Ignore()
    {
    }
}
```
<sup><a href='/samples/StateAlchemist.Samples/Door/Door.cs#L63-L136' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-door-decision' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Three things are worth reading twice:

- `Handle` lists the events the machine accepts while the decision is pending. What ends the wait is *leaving* the
  pending state, so `Abandon` names a target; a stay would keep waiting.
- Leaving cancels the `CancellationToken` the reader was given.
- A reader that throws fires `DecisionFailed`, which is an ordinary trigger to recover from.

```csharp
await door.FireAsync(new Badge(7));           // completes when the reader has answered and the door has opened
```

## Reading a pipe

The shape a protocol parser wants: bytes arrive in whatever chunks the socket gives, a whole stretch of text is
one call, and something else fires events meanwhile.

<!-- snippet: sample-lines-states -->
<a id='snippet-sample-lines-states'></a>
```cs
/// <summary>The stream. Its data lasts as long as the machine.</summary>
public struct Stream : IRootState
{
    /// <summary>Completed lines.</summary>
    public int Count;

    /// <summary>The length of the line that just ended, for the action that reports it.</summary>
    public int LastLength;
}

/// <summary>Reading a line. What it has read so far belongs to the line, and goes when the line ends.</summary>
[Initial]
public struct Line : IState<Stream>
{
    /// <summary>Bytes in the line so far.</summary>
    public int Length;
}
```
<sup><a href='/samples/StateAlchemist.Samples/Lines/LineReader.cs#L26-L44' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-lines-states' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample-lines-module -->
<a id='snippet-sample-lines-module'></a>
```cs
[Module]
public static class LineModule
{
    /// <summary>
    /// A run: every byte that is not a newline is handled the same way, so the machine takes the whole stretch
    /// in one call. The stop set — here, just the newline — is worked out at compile time from the other
    /// transitions in this state.
    /// </summary>
    [Transition(From = typeof(Line)), OnAny, Run]
    public static void Text(ref Line line, ReadOnlySpan<byte> run) => line.Length += run.Length;

    /// <summary>
    /// The newline ends a line. This is a stay, not a re-entry: a stay keeps the state's data, so the transform
    /// resets the length itself and the machine never leaves <see cref="Line"/>. A re-entry would clear the data
    /// for you, and warn that it had ([`SALCH0301`](../../docs/reference/diagnostics.md#salch0301)).
    /// </summary>
    [Transition(From = typeof(Line)), On((byte)'\n')]
    public static class EndOfLine
    {
        public static void Transform(ref Line line, ref Stream stream)
        {
            stream.Count++;
            stream.LastLength = line.Length;
            line.Length = 0;
        }

        public static void Completed(Lines lines, Stream stream) => lines.Lengths.Add(stream.LastLength);
    }

    /// <summary>An event from elsewhere. It waits its turn like any other input.</summary>
    [Transition(From = typeof(Stream)), OnEvent(typeof(Flush))]
    public static void Flushed(Lines lines) => lines.Flushes++;
}
```
<sup><a href='/samples/StateAlchemist.Samples/Lines/LineReader.cs#L46-L80' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-lines-module' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

<!-- snippet: sample-lines-machine -->
<a id='snippet-sample-lines-machine'></a>
```cs
/// <summary>
/// <c>Serialized</c>: the read loop and whatever fires events are different threads, and the machine runs one
/// transition at a time whichever of them arrives first.
/// </summary>
[Machine(Root = typeof(Stream), Value = typeof(byte), Context = typeof(Lines), Concurrency = Concurrency.Serialized)]
[Include(typeof(LineModule))]
public sealed partial class LineReader;
```
<sup><a href='/samples/StateAlchemist.Samples/Lines/LineReader.cs#L82-L90' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-lines-machine' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The read loop is the ordinary one. Awaiting `FireAsync` is the backpressure:

<!-- snippet: sample-lines-loop -->
<a id='snippet-sample-lines-loop'></a>
```cs
/// <summary>
/// Reads until the pipe completes. Awaiting <c>FireAsync</c> is the backpressure: while the machine is busy —
/// including while a decision of its own is pending — this loop is not reading, so the pipe fills, and once it
/// passes its pause threshold the writer waits.
/// </summary>
public static async Task ReadAsync(PipeReader reader, LineReader machine, CancellationToken cancellation = default)
{
    while (true)
    {
        var read = await reader.ReadAsync(cancellation).ConfigureAwait(false);
        foreach (var segment in read.Buffer)
        {
            await machine.FireAsync(segment).ConfigureAwait(false);
        }

        reader.AdvanceTo(read.Buffer.End);
        if (read.IsCompleted)
        {
            break;
        }
    }

    await reader.CompleteAsync().ConfigureAwait(false);
}
```
<sup><a href='/samples/StateAlchemist.Samples/Lines/LineReader.cs#L95-L120' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-lines-loop' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
