# Runs

Most bytes on a telnet connection are ordinary text or subnegotiation payload, and a state handles every one of
them the same way: "append it". Dispatching a `switch` per byte for that is wasted work. A **run transition**
takes the whole stretch at once.

```csharp
[Transition(From = typeof(GmcpPayload)), OnAny, Run]
public static void Capture(ref GmcpPayload self, ReadOnlySpan<byte> run) => self.Buffer.Append(run);
```

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

## Rules

- A run is a **stay** on `[OnAny]` or a range.
- Its transform takes exactly `(ref TState self, ReadOnlySpan<TValue> run)`, optionally followed by
  `in TConfig` and the context.
- It has no `Guard`: a guard per value would defeat the point.
- Its `Completed`, if any, receives the run as `ReadOnlyMemory<TValue>`.

Anything else is [`SALCH0701`](../reference/diagnostics.md#salch0701).

A single value fired with `FireAsync(TValue)` still reaches a run transition, as a run of length one: runs change
how fast a batch is consumed, never what it does.
