# Getting started

This guide builds a small telnet machine: it reads text, notices `IAC`, accepts GMCP, refuses every other option,
and reads the client's window size from a NAWS subnegotiation. The same machine is the running example in every
concept page.

## 1. Declare the states

A state is a `public struct`; its fields are its data; its parent is named by `IState<TParent>`.

```csharp
public struct Connected : IRootState          // the root: lives as long as the connection
{
    public bool GmcpEnabled;
    public int Width;
    public int Height;
}

[Initial]                                     // where the machine starts
public struct Idle : IState<Connected> { public int LineLength; }

public struct Command : IState<Connected> { }        // after IAC
[Initial] public struct AwaitingVerb : IState<Command> { }
public struct Willing : IState<Command> { }          // after IAC WILL

public struct SubNegotiation : IState<Connected> { public byte Option; }   // after IAC SB
[Initial] public struct AwaitingOption : IState<SubNegotiation> { }

public struct Naws : IState<SubNegotiation>
{
    public byte[]? Bytes;
    public int Index;
    public void Reset() => Index = 0;         // keep the buffer when NAWS is left
}

public struct NawsEscaping : IState<SubNegotiation> { public Naws Captured; }
```

The tree:

```
Connected (root)
├── Idle [Initial]
├── Command
│   ├── AwaitingVerb [Initial]
│   └── Willing
└── SubNegotiation
    ├── AwaitingOption [Initial]
    ├── Naws
    └── NawsEscaping
```

## 2. Declare transitions in a module

A module is a static class marked `[Module]`. A transition is a method (when it only changes data) or a static
class (when it has more than one part).

```csharp
[Module]
public static class TelnetCore
{
    [Transition(From = typeof(Idle)), OnAny]                       // stay: count text
    public static void Text(ref Idle self, byte value) => self.LineLength++;

    [Transition(From = typeof(Idle)), On(LineFeed)]
    public static void EndOfLine(ref Idle self) => self.LineLength = 0;

    [Transition(From = typeof(Idle), To = typeof(Command)), On(Iac)]
    public static void BeginCommand(in Idle from) { }

    [Transition(From = typeof(AwaitingVerb), To = typeof(Willing)), On(Will)]
    public static void BeginWilling(in AwaitingVerb from, ref Willing to) { }

    [Transition(From = typeof(Willing), To = typeof(Idle)), OnAny]  // refuse anything nobody accepts
    public static class Refuse
    {
        public static ValueTask CompletedAsync(TelnetContext context, byte option) =>
            context.SendAsync(Iac, Dont, option);
    }
}
```

Another module — here, a GMCP plugin — can add a transition out of `Willing` without touching `TelnetCore`:

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

`On(GmcpOption)` is an exact value, so it wins over `TelnetCore.Refuse`'s `[OnAny]` in the same state.

## 3. Declare the machine

The application names the root, the value type, the context, and the modules:

```csharp
[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext))]
[Include(typeof(TelnetCore)), Include(typeof(GmcpModule)), Include(typeof(NawsModule))]
public sealed partial class SampleTelnet;
```

The generator fills in `SampleTelnet`: its storage, a `switch` for every state and byte, and the members listed
in the [generated API](../reference/generated-api.md).

## 4. Run it

```csharp
var context = new TelnetContext();
await using var telnet = new SampleTelnet(context);
await telnet.StartAsync();                       // runs [Entered] actions on the initial path

await telnet.FireAsync(new byte[] { Iac, Will, GmcpOption });
// context.Sent now holds IAC DO GMCP, and telnet.TryGetConnected(out var root) shows root.GmcpEnabled == true.
```

On a socket, feed the machine whole reads and await each one: `FireAsync` completes when the bytes have been
processed, waiting through any [decision](../concepts/decisions.md) they start, so the ordinary read loop gets
backpressure without doing anything about it.

## Next

- [States](../concepts/states.md) and [transitions](../concepts/transitions.md) explain what you just declared.
- [Testing a machine](testing.md) shows how to test all of it without a socket.
