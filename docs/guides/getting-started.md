# Getting started

This guide builds a small telnet machine: it reads text, notices `IAC`, accepts GMCP, refuses every other option,
and reads the client's window size from a NAWS subnegotiation. The same machine is the running example in every
concept page.

## 1. Declare the states

A state is a `public struct`; its fields are its data; its parent is named by `IState<TParent>`.

<!-- snippet: sample-states -->
<a id='snippet-sample-states'></a>
```cs
/// <summary>The root: lives as long as the connection.</summary>
public struct Connected : IRootState
{
    public bool GmcpEnabled;
    public int Width;
    public int Height;
}

/// <summary>Reading ordinary text. Where the machine starts.</summary>
[Initial]
public struct Idle : IState<Connected>
{
    public int LineLength;
}

/// <summary>After IAC: a command follows.</summary>
public struct Command : IState<Connected>
{
}

[Initial]
public struct AwaitingVerb : IState<Command>
{
}

/// <summary>After IAC WILL: the option follows.</summary>
public struct Willing : IState<Command>
{
}

/// <summary>After IAC SB: a subnegotiation follows.</summary>
public struct SubNegotiation : IState<Connected>
{
    public byte Option;
}

[Initial]
public struct AwaitingOption : IState<SubNegotiation>
{
}

/// <summary>Collecting NAWS's four bytes.</summary>
public struct Naws : IState<SubNegotiation>
{
    public byte[]? Bytes;
    public int Index;

    /// <summary>Rewinds but keeps the buffer, so entering NAWS again allocates nothing.</summary>
    public void Reset() => Index = 0;
}

/// <summary>After IAC inside NAWS: SE ends it.</summary>
public struct NawsEscaping : IState<SubNegotiation>
{
    public Naws Captured;
}
```
<sup><a href='/samples/StateAlchemist.Samples/Telnet/States.cs#L3-L62' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-states' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

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

<!-- snippet: sample-telnet-core -->
<a id='snippet-sample-telnet-core'></a>
```cs
[Module]
public static class TelnetCore
{
    /// <summary>Ordinary text: stay in Idle and count the line.</summary>
    [Transition(From = typeof(Idle)), OnAny]
    public static void Text(ref Idle self, byte value) => self.LineLength++;

    [Transition(From = typeof(Idle)), On(LineFeed)]
    public static void EndOfLine(ref Idle self) => self.LineLength = 0;

    [Transition(From = typeof(Idle), To = typeof(Command)), On(Iac)]
    public static void BeginCommand(in Idle from)
    {
    }

    [Transition(From = typeof(AwaitingVerb), To = typeof(Willing)), On(Will)]
    public static void BeginWilling(in AwaitingVerb from, ref Willing to)
    {
    }

    [Transition(From = typeof(AwaitingVerb), To = typeof(SubNegotiation)), On(Sb)]
    public static void BeginSubNegotiation(in AwaitingVerb from, ref SubNegotiation to)
    {
    }

    [Transition(From = typeof(AwaitingVerb), To = typeof(Idle)), OnAny]
    public static void UnknownCommand(in AwaitingVerb from)
    {
    }

    /// <summary>A subnegotiation no module understands is abandoned.</summary>
    [Transition(From = typeof(SubNegotiation), To = typeof(Idle)), OnAny]
    public static void Abandon(in SubNegotiation from)
    {
    }

    /// <summary>Any option no module accepts is refused.</summary>
    [Transition(From = typeof(Willing), To = typeof(Idle)), OnAny]
    public static class Refuse
    {
        public static ValueTask CompletedAsync(TelnetContext context, byte option) => context.SendAsync(Iac, Dont, option);
    }

    /// <summary>Declared on the root, so it applies in every state.</summary>
    [Transition(From = typeof(Connected), To = typeof(Idle)), OnEvent(typeof(Error))]
    public static void Recover(in Error error)
    {
    }

    [Entered(typeof(Idle))]
    public static void Ready(TelnetContext context) => context.Log.Add("ready");
}
```
<sup><a href='/samples/StateAlchemist.Samples/Telnet/TelnetCore.cs#L7-L70' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-telnet-core' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Another module — here, a GMCP plugin — can add a transition out of `Willing` without touching `TelnetCore`:

<!-- snippet: sample-gmcp-module -->
<a id='snippet-sample-gmcp-module'></a>
```cs
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
<sup><a href='/samples/StateAlchemist.Samples/Telnet/GmcpModule.cs#L7-L21' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-gmcp-module' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`On(GmcpOption)` is an exact value, so it wins over `TelnetCore.Refuse`'s `[OnAny]` in the same state.

## 3. Declare the machine

The application names the root, the value type, the context, and the modules:

<!-- snippet: sample-machine -->
<a id='snippet-sample-machine'></a>
```cs
[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext))]
[Include(typeof(TelnetCore)), Include(typeof(GmcpModule)), Include(typeof(NawsModule))]
public sealed partial class SampleTelnet
{
}
```
<sup><a href='/samples/StateAlchemist.Samples/Telnet/SampleTelnet.cs#L3-L9' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-machine' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

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
