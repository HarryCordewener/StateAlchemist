# Machines

## Modules

A module is a static class marked `[Module]`. It groups transitions, decisions and state actions; a protocol
plugin is typically one module. Modules can live in any library and can add transitions out of states that other
modules declared.

```csharp
[Module]
public static class GmcpModule
{
    [Transition(From = typeof(Willing), To = typeof(Idle)), On(GmcpOption)]
    public static class Accept
    {
        public static void Transform(ref Connected root) => root.GmcpEnabled = true;
        public static ValueTask CompletedAsync(TelnetContext context) => context.SendAsync(Iac, Do, GmcpOption);
    }
}
```

Every method a machine calls must be `public static`, because the generated code lives in the application
([`SALCH0002`](../reference/diagnostics.md#salch0002)); analyzers in the declaring library report that where the
method is written.

## The machine declaration

An application declares a machine on a `partial class`:

```csharp
[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext))]
[Include(typeof(TelnetCore)), Include(typeof(GmcpModule)), Include(typeof(NawsModule))]
public sealed partial class SampleTelnet;
```

The generator runs in the application and sees every included module, from every library, at once. It emits
one merged `switch` for exactly that set of modules. That is why conflicts between plugins — two modules claiming
the same option in the same state — are compile errors in the application
([`SALCH0101`](../reference/diagnostics.md#salch0101)) instead of surprises at runtime.

Modules are chosen when the application compiles. An application that needs several configurations — a client and
a server, say — declares several machine types.

## Options

| Option | Default | Meaning |
|---|---|---|
| `Root` | required | The root state. |
| `Value` | required | The value-trigger type: an integral type or an enum of 16 bits or fewer. |
| `Context` | none | Your object, passed to actions, decisions, and — unless `Purity` is `Strict` — guards and transforms. |
| `Config` | none | Immutable configuration, passed as `in TConfig` to anything that asks for it. |
| `Concurrency` | `Checked` | How concurrent callers are handled; see [concurrency](concurrency.md). |
| `InboxCapacity` | 0 | For `Serialized`: a bounded inbox, so event producers wait instead of queueing without limit. |
| `Purity` | `Permissive` | `Strict` forbids guards, transforms and completions from taking the context. |
| `Unhandled` | `Ignore` | `Throw` throws `UnhandledTriggerException` when nothing handles a trigger. |

A missing `Root` or `Value` is [`SALCH0107`](../reference/diagnostics.md#salch0107).

### Purity

With `Purity.Strict`, a transition's `Guard`, `Transform` and a decision's `Complete` depend only on state data,
the trigger and the configuration. That makes the machine's [pure layer](../guides/testing.md#2-the-pure-layer)
exact: `Plan()` predicts what a trigger will do with no outside influence, and a transform can be tested with
nothing but values. Taking the context then is [`SALCH0205`](../reference/diagnostics.md#salch0205). With the
default, `Permissive`, the context is allowed, and the machine's definition records which transitions use it.

## What one machine costs

A machine *instance* is its state data — one slot per state — plus a few fields: the active leaf, a pending
decision, a small event queue. The machine *definition* is generated code and static data, shared by every
instance. Creating a machine for a new connection builds nothing.
