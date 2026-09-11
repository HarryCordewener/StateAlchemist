# Testing a machine

A StateAlchemist machine can be tested at three levels, from the smallest to the whole.

## 1. Transforms are plain functions

A transform is a `public static void` method over structs. Test it by calling it:

```csharp
[Test]
public async Task FinishReadsTheWindowSize()
{
    var escaping = new NawsEscaping { Captured = new Naws { Bytes = [0, 80, 0, 24], Index = 4 } };
    var root = new Connected();

    NawsModule.Finish.Transform(in escaping, ref root);

    await Assert.That((root.Width, root.Height)).IsEqualTo((80, 24));
}
```

No machine, no context, no mocks. Guards are the same: `NawsModule.Finish.Guard(in escaping)`.

## 2. The pure layer

`Plan(trigger)` says what a trigger *would* do from the machine's current state — which transition, what it exits
and enters — evaluating guards but running nothing:

```csharp
var plan = telnet.Plan(TelnetBytes.Iac);
await Assert.That(plan.Transition).IsEqualTo("TelnetCore.BeginCommand");
await Assert.That(plan.Target).IsEqualTo(typeof(AwaitingVerb));
await Assert.That(string.Join(",", plan.Exiting.Select(t => t.Name))).IsEqualTo("Idle");
```

`Definition` describes the whole machine as data — states, parents, transitions, triggers — for tests that check
structure ("every `Willing` refusal is an `[OnAny]`") and for diagrams. With
[`Purity.Strict`](../concepts/machines.md#purity), the pure layer is exact: nothing outside the machine's data,
the trigger and the configuration can change what `Plan` predicts.

## 3. The machine interface

Every machine implements `IMachine<TValue>`: start, fire, stop, inspect. Write behaviour tests against the
interface and a context that records what the actions did:

```csharp
[Test]
public async Task GmcpIsAcceptedAndEverythingElseRefused()
{
    var context = new TelnetContext();
    IMachine<byte> telnet = new SampleTelnet(context);
    await telnet.StartAsync();

    await telnet.FireAsync(new byte[] { Iac, Will, GmcpOption, Iac, Will, 42 });

    await Assert.That(string.Join(" | ", context.Sent.Select(s => string.Join(",", s))))
        .IsEqualTo("255,253,201 | 255,254,42");
    await Assert.That(telnet.TryGetState(out Connected root) && root.GmcpEnabled).IsTrue();
}
```

Tests written against `IMachine<TValue>` do not care what implements it — which is how StateAlchemist tests
itself.

## How StateAlchemist tests itself

The library ships with a **reference interpreter**: a slow, reflection-based `IMachine<TValue>` that runs the same
declarations by the book. The semantics in these docs are written as a **contract test suite** against
`IMachine<TValue>`, and the suite runs twice: against the reference interpreter, and against the generated
machine. A generated machine that behaves differently from the interpreter fails the same test.

The reference interpreter exists for testing StateAlchemist and your declarations, not for production. It
allocates freely and runs orders of magnitude slower than generated code.
