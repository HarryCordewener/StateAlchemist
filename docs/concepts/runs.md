# Runs

Most bytes on a telnet connection are ordinary text or subnegotiation payload, and a state handles every one of
them the same way: "append it". Dispatching a `switch` per byte for that is wasted work. A **run transition**
takes the whole stretch at once.

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

When the active leaf has a run transition, the batch `FireAsync(ReadOnlyMemory<TValue>)` scans ahead to the next
value some other transition handles first — the **stop set** — and hands everything before it to the transform in
one call. On `net8.0` and later the scan uses `SearchValues<T>`, which is vectorised; on `netstandard2.0` it is a
plain loop. Then it dispatches the stop value normally and carries on.

## The stop set

The stop set is computed at compile time from the same rules that pick a transition: a value stops the run if,
from the active leaf, some other transition would be tried before the run for it. For `GmcpPayload` with an
`[OnAny]` run and an `[On(Iac)]` escape, the stop set is `{ IAC }`: the machine hands over everything up to the
next `IAC` in one call.

For telnet this covers the two hot paths:

| State | Run | Stop set |
|---|---|---|
| reading text | `[OnAny]`, appending to the line | `{ IAC, LF }` |
| a GMCP, MSDP or MSSP payload | `[OnAny]`, appending to the payload | `{ IAC }` |

### What an ancestor handles does not stop the run

Resolution starts at the active leaf, so a state's own trigger beats an ancestor's — that is what makes `[OnAny]`
a state's "or else". A run makes the ordinary rule sharp: the ancestor's value does not merely lose to the run,
it is taken *into* the run, and nothing ends it.

```csharp
[Transition(From = typeof(Line), To = typeof(Idle)), On((byte)'\n')]   // on the parent
public static void EndOfLine() { }

[Transition(From = typeof(Reading)), OnAny, Run]                       // in the child: the newline vanishes
public static void Text(ref Reading self, ReadOnlySpan<byte> run) => self.Length += run.Length;
```

`Reading`'s stop set is empty, and the machine never leaves it. Declare the trigger on the run's own state as
well and it stops the run there; [`SALCH0702`](../reference/diagnostics.md#salch0702) says so at compile time.

## Rules

- A run is a [**stay**](transitions.md#three-kinds) on `[OnAny]` or a range — it cannot change state, because
  the bytes after the first are only the same trigger for as long as the state has not changed.
- Its transform takes exactly `(ref TState self, ReadOnlySpan<TValue> run)`, optionally followed by
  `in TConfig` and the context.
- It has no `Guard`: a guard per value would defeat the point.
- Its `Completed`, if any, receives the run as `ReadOnlyMemory<TValue>`.

Anything else is [`SALCH0701`](../reference/diagnostics.md#salch0701).

A single value fired with `FireAsync(TValue)` still reaches a run transition, as a run of length one: runs change
how fast a batch is consumed, never what it does.
