# Testing a machine

A StateAlchemist machine can be tested at three levels, from the smallest to the whole.

## 1. Transforms are plain functions

A transform is a `public static void` method over structs. Test it by calling it:

<!-- snippet: sample-test-transform -->
<a id='snippet-sample-test-transform'></a>
```cs
[Test]
public async Task FinishReadsTheWindowSize()
{
    var escaping = new NawsEscaping { Captured = new Naws { Bytes = [0, 80, 0, 24], Index = 4 } };
    var root = new Connected();

    NawsModule.Finish.Transform(in escaping, ref root);

    await Assert.That((root.Width, root.Height)).IsEqualTo((80, 24));
}
```
<sup><a href='/tests/StateAlchemist.Generated.Tests/DocumentationExampleTests.cs#L75-L86' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-test-transform' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

No machine, no context, no mocks. Guards are the same: `NawsModule.Finish.Guard(in escaping)`.

## 2. The pure layer

`Plan(trigger)` says what a trigger *would* do from the machine's current state — which transition, what it exits
and enters — evaluating guards but running nothing:

<!-- snippet: sample-test-plan -->
<a id='snippet-sample-test-plan'></a>
```cs
var plan = telnet.Plan(TelnetBytes.Iac);
await Assert.That(plan.Transition).IsEqualTo("TelnetCore.BeginCommand");
await Assert.That(plan.Target).IsEqualTo(typeof(AwaitingVerb));
await Assert.That(string.Join(",", plan.Exiting.Select(t => t.Name))).IsEqualTo("Idle");
```
<sup><a href='/tests/StateAlchemist.Generated.Tests/DocumentationExampleTests.cs#L95-L100' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-test-plan' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`Refused` names the guarded transitions whose guard returned `false` on the way, in the order they were tried,
which is what an interface needs to say why a trigger would not fire. A trigger nothing handles has a plan whose
`Handled` is `false`, and its `Refused` still lists the guards that were tried.

`Definition` describes the whole machine as data — states, parents, transitions, triggers, and a decision's
outcomes with the state each one moves to — for tests that check structure ("every `Willing` refusal is an
`[OnAny]`") and for diagrams. With
[`Purity.Strict`](../concepts/machines.md#purity), the pure layer is exact: nothing outside the machine's data,
the trigger and the configuration can change what `Plan` predicts.

## 3. The machine interface

Every machine implements `IMachine<TValue>`: start, fire, stop, inspect. Write behaviour tests against the
interface and a context that records what the actions did:

<!-- snippet: sample-test-machine -->
<a id='snippet-sample-test-machine'></a>
```cs
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
<sup><a href='/tests/StateAlchemist.Generated.Tests/DocumentationExampleTests.cs#L103-L117' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-test-machine' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Tests written against `IMachine<TValue>` do not care what implements it — which is how StateAlchemist tests
itself.

## How StateAlchemist tests itself

The library ships with a **reference interpreter**: Stateless, a reflection-based `IMachine<TValue>` that runs the same
declarations by the book. The semantics in these docs are written as a **contract test suite** against
`IMachine<TValue>`, and the suite runs twice: against the reference interpreter, and against the generated
machine. A generated machine that behaves differently from the interpreter fails the same test.

The reference interpreter exists for testing StateAlchemist and your declarations, not for production.
