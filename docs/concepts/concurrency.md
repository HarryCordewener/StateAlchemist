# Concurrency

A machine processes one transition at a time. What differs is who may call it, and what happens if two callers
try at once. The choice is made when the application compiles — `[Machine(Concurrency = ...)]` — and the
generator emits only that mode's code.

| Mode | Who may fire | If two callers overlap | Cost per call |
|---|---|---|---|
| **`Checked`** (default) | one caller at a time | the second throws `ConcurrentUseException`; state is never corrupted | +4.9 ns |
| `Unchecked` | one caller at a time, promised by the host | undefined: state can be corrupted | none |
| `Serialized` | any thread | nothing goes wrong: calls queue and run one at a time | +65 ns |

Costs are what a stay costs above `Unchecked`'s 5.7 ns, measured on .NET 11 RC1, x64, uncontended, by the
benchmarks in `benchmarks/StateAlchemist.Benchmarks`. No mode allocates.

## Checked

The default follows the lead of .NET's `Dictionary`, which detects concurrent modification and throws rather than
corrupting itself: wrong use fails loudly, and correct use pays one interlocked operation per call. Use it unless
you have measured a reason not to.

## Unchecked

For a host that guarantees a single caller — a connection's read loop, say — and wants the last few nanoseconds.
This is the same trade as `Channel`'s `SingleReader`/`SingleWriter`: a promise the host makes in exchange for a
faster path. An `Unchecked` machine with async actions draws warning
[`SALCH0209`](../reference/diagnostics.md#salch0209): continuations run on other threads, so even a single caller
must await every `FireAsync` before the next.

## Serialized

Any thread may call `FireAsync`. Each call becomes an *input* in the machine's inbox, and whichever caller finds
the machine idle runs it straight away — inline, on the calling thread, with no background consumer task and no
thread hop when nothing is contended. Each transition, **including its awaited actions**, completes before the
next begins. A caller whose trigger queued behind another waits on its input, which is itself the pooled
`IValueTaskSource` the caller's `ValueTask` is built on: it completes when that caller's own triggers are done,
and carries the exception if one throws. A call that finishes without suspending allocates nothing at all —
the input goes back to the pool before `FireAsync` returns.

This is the turn-based model of Orleans grains. It is also what a lock around a machine cannot give you: a lock
around `FireAsync` stops protecting anything once actions are async.

The inbox is a list and a lock, not a `Channel`: a channel's own completion sources and its bounded mode cost
more than the whole rest of the call, and the machine needs neither — it has one reader by construction, and the
inputs it hands out are the completion sources.

### Bounded inbox

`InboxCapacity = n` caps how many inputs may wait: producers that outrun the machine wait for room instead of
growing the queue. Room is a `SemaphoreSlim`, taken when an input joins the inbox and released when the pump takes
it out, so a producer that has to wait does so asynchronously. A bounded machine always goes through its inbox —
it cannot take the inline path, which would skip the accounting — so it costs more per call even when nothing is
contended. That is why it is opt-in.

## While a decision is pending

While a [decision](decisions.md) is pending, the caller whose input started it is awaiting its `FireAsync`, and
the decision completes on another thread. So the machine accepts **events** from other callers in every mode —
`Checked` and `Unchecked` included — through the same inbox, which is why a machine with an async decision has one
whatever its concurrency mode. That is what lets a disconnect or a timeout reach a pending decision.

Events that waited run as soon as the decision resolves, in the order they arrived, before the rest of the paused
input.

Values are different: they come from one stream, so a second caller firing values while a decision is pending is
misuse in every mode — `Checked` throws, `Serialized` queues them behind the waiting batch.

## What the inbox costs

A machine with an inbox — `Serialized`, or any mode with an async decision — costs about 65 ns per call more than
one without, for the same stay. The inbox is what buys the guarantee: every call is an object with an identity
that outlives the calling stack, so it can be queued, completed later, and completed exactly once. Most of the
cost is the two lock regions a call passes through, claiming the pump and releasing it.

Every guard is paid per call, not per value: the batch `FireAsync(ReadOnlyMemory<TValue>)` pays it once for a
whole read, which is why a machine reading a socket should hand the buffer over whole.
