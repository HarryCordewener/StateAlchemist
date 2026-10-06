# Hosting a machine

`StateAlchemist.Hosting` runs a machine for the lifetime of a .NET Generic Host: started with the host, fed from a
queue, and stopped when the host shuts down. It is a separate package so that `StateAlchemist` itself keeps no
dependencies.

```bash
dotnet add package StateAlchemist.Hosting
```

It references `StateAlchemist`, and the generator comes with it.

## Registering a machine

<!-- snippet: hosting-register -->
<a id='snippet-hosting-register'></a>
```cs
builder.Services.AddSingleton<PhoneLog>();
builder.Services.AddHostedMachine<PhoneCall, Button>(
    services => new PhoneCall(services.GetRequiredService<PhoneLog>()));
```
<sup><a href='/tests/StateAlchemist.Hosting.Tests/HostedMachineTests.cs#L208-L212' title='Snippet source file'>snippet source</a> | <a href='#snippet-hosting-register' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This registers three singletons:

| Service | What it is |
|---|---|
| `PhoneCall` | the machine, created by the factory. The host starts it; the factory must not. |
| `MachineInbox<PhoneCall, Button>` | the queue the machine is fed from |
| a hosted service | starts the machine, fires what the inbox holds one input at a time, and stops the machine |

A machine type is registered once; a second `AddHostedMachine` for the same type throws.

## Feeding it

Anything that wants to fire the machine asks for its inbox and writes to it:

<!-- snippet: hosting-write -->
<a id='snippet-hosting-write'></a>
```cs
var inbox = host.Services.GetRequiredService<MachineInbox<PhoneCall, Button>>();
await inbox.WriteAsync(Button.Lift);
await inbox.WriteAsync(Button.Answered);
await inbox.WriteAsync(Button.Second);
await inbox.WriteAsync(Button.Second);
```
<sup><a href='/tests/StateAlchemist.Hosting.Tests/HostedMachineTests.cs#L52-L58' title='Snippet source file'>snippet source</a> | <a href='#snippet-hosting-write' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`WriteAsync` completes when the input is queued, not when the machine has processed it. The service awaits each
`FireAsync` before reading the next input, so the machine has a single caller and inputs are fired in the order
they were written.

An inbox is unbounded unless `AddHostedMachine` is given a `capacity`. A bounded inbox fills while the machine is
busy, for example waiting on a [decision](../concepts/decisions.md#deferral-and-backpressure), and then
`WriteAsync` waits for room and `TryWrite` returns `false`. That is the machine's backpressure reaching the
producers.

## Events and other inputs

An inbox holds one type. To feed it events, or anything that has to be translated first, say how an input is
fired:

<!-- snippet: hosting-events -->
<a id='snippet-hosting-events'></a>
```cs
builder.Services.AddHostedMachine<CardDoor, byte, Badge>(
    _ => new CardDoor(access),
    (door, badge) => door.FireAsync(badge),
    capacity);
```
<sup><a href='/tests/StateAlchemist.Hosting.Tests/HostedMachineTests.cs#L225-L230' title='Snippet source file'>snippet source</a> | <a href='#snippet-hosting-events' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The second type argument is the machine's value type, which C# cannot infer from the machine.

## The host's lifetime

| Host | Machine |
|---|---|
| starting | `StartAsync` runs the initial path's `[Entered]` actions. The host is not started until they have run, and one that throws fails the start. |
| running | each input written to the inbox is fired in turn |
| stopping | the inbox stops accepting inputs (`TryWrite` returns `false`, `WriteAsync` throws `ChannelClosedException`), the inputs already queued are fired, then `StopAsync` runs the `[Exited]` actions |
| shutdown timeout reached while stopping | inputs still queued are dropped, and `StopAsync` runs anyway, which cancels a pending decision |

An exception from firing ends the service, and the host does what `HostOptions.BackgroundServiceExceptionBehavior`
says. Its default, `StopHost`, stops the host.

See [lifecycle](../concepts/lifecycle.md) for what starting and stopping do inside the machine.
