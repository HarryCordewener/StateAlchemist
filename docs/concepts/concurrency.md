# Concurrency

A machine processes one transition at a time. What differs is who may call it, and what happens if two callers
try at once. The choice is made when the application compiles — `[Machine(Concurrency = ...)]` — and the
generator emits only that mode's code.

| Mode | Who may fire | If two callers overlap | Cost per call |
|---|---|---|---|
| **`Checked`** (default) | one caller at a time | the second throws `ConcurrentUseException`; state is never corrupted | +6.5 ns |
| `Unchecked` | one caller at a time, promised by the host | undefined: state can be corrupted | none |
| `Serialized` | any thread | nothing goes wrong: calls queue and run one at a time | +20.7 ns |

Costs were measured on .NET 11 RC1, x64, uncontended, for the smallest possible transition (0.6 ns on its own).

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

Any thread may call `FireAsync`. Each call is written to a `Channel` created with `SingleReader = true`, and
whichever caller finds the machine idle drains it straight away — no background consumer task, and no thread hop
when nothing is contended. Each transition, **including its awaited actions**, completes before the next begins.
A caller whose trigger queued behind another waits on a pooled `ValueTask` source that completes when its own
transition does, and carries that transition's exception if it throws.

This is the turn-based model of Orleans grains. It is also what a lock around a machine cannot give you: a lock
around `FireAsync` stops protecting anything once actions are async.

### Bounded inbox

`InboxCapacity = n` makes the inbox a bounded channel with `FullMode = Wait`: producers that outrun the machine
wait instead of growing the queue. A bounded channel takes a lock internally, so it costs +47.7 ns per call —
which is why it is opt-in.

## While a decision is pending

While a [decision](decisions.md) is pending, the caller whose input started it is awaiting its `FireAsync`, and
the decision completes on another thread. So the machine accepts **events** from other callers in every mode —
`Checked` and `Unchecked` included — through the same `Channel` inbox. That is what lets a disconnect or a timeout
reach a pending decision. The cost exists only while a decision is pending.

Values are different: they come from one stream, so a second caller firing values while a decision is pending is
misuse in every mode — `Checked` throws, `Serialized` queues them behind the waiting batch.

## Measured alternatives

For comparison, on the same machine: a raw `ConcurrentQueue` inbox +18.3 ns, `System.Threading.Lock` +13 ns,
`Monitor` +15 ns, `SemaphoreSlim.WaitAsync` +32 ns. The `Channel` costs 2.4 ns more than the raw queue it is built
on, for standard completion semantics and the bounded option. Every guard is paid per call, not per value: the
batch `FireAsync(ReadOnlyMemory<TValue>)` pays it once for a whole read.
