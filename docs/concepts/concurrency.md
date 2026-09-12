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
the machine idle runs it on the calling thread: no background consumer task, and no thread hop when nothing is
contended. Each transition, **including its awaited actions**, completes before the next begins. A caller whose
trigger queued behind another waits on its input, which is the pooled `IValueTaskSource` behind that caller's
`ValueTask`: it completes when that caller's own triggers are done, and carries the exception if one throws. A
call that never suspends allocates nothing, because the input returns to the pool before `FireAsync` does.

This is the turn-based model of Orleans grains, and it is what a lock cannot give you: a lock around `FireAsync`
stops protecting anything once actions are async.

The inbox is a list and a lock rather than a `Channel`. A channel brings its own completion sources and a bounded
mode that costs more than the rest of the call, and the machine needs neither: it has one reader by construction,
and the inputs it hands out are already completion sources.

### Bounded inbox

`InboxCapacity = n` caps how many inputs may wait: producers that outrun the machine wait for room instead of
growing the queue. Room is a `SemaphoreSlim`, taken when an input joins the inbox and released when the pump takes
it out, so a producer that waits does so asynchronously. A bounded machine always goes through its inbox — the
inline path would skip the accounting — so it costs more per call even uncontended. Hence opt-in.

## While a decision is pending

While a [decision](decisions.md) is pending, the caller whose input started it is awaiting its `FireAsync`, and
the decision completes on another thread. The machine therefore accepts **events** from other callers in every
mode, `Checked` and `Unchecked` included, through the same inbox — which is why a machine with an async decision
has an inbox whatever its concurrency mode. This is how a disconnect or a timeout reaches a pending decision.

Events that waited run as soon as the decision resolves, in the order they arrived, before the rest of the paused
input.

Values come from one stream, so a second caller firing values while a decision is pending is misuse in every
mode: `Checked` throws, `Serialized` queues them behind the waiting batch.

## What the inbox costs

A machine with an inbox — `Serialized`, or any mode with an async decision — costs about 65 ns per call more than
one without, for the same stay. That buys the guarantee: every call is an object whose identity outlives the
calling stack, so it can be queued, completed later, and completed exactly once. Most of the cost is the two lock
regions a call passes through, claiming the pump and releasing it.

Every guard is paid per call, not per value. The batch `FireAsync(ReadOnlyMemory<TValue>)` pays it once for a
whole read, so a machine reading a socket should hand the buffer over whole.
