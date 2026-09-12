# Writing a module

A module adds behaviour to a machine it does not own. This guide adds NAWS — reading the client's window size —
to the telnet machine without changing `TelnetCore`.

## 1. Add your states under the ones you extend

NAWS happens inside a subnegotiation, so its states are children of `SubNegotiation`, which the core declares:

```csharp
public struct Naws : IState<SubNegotiation>
{
    public byte[]? Bytes;
    public int Index;
    public void Reset() => Index = 0;
}

public struct NawsEscaping : IState<SubNegotiation> { public Naws Captured; }
```

Your states can live in your own library. The machine includes them because your transitions name them.

## 2. Add transitions out of states you do not own

`AwaitingOption` belongs to the core. Your module adds a transition out of it, on your option's byte:

<!-- snippet: sample-naws-module -->
<a id='snippet-sample-naws-module'></a>
```cs
[Module]
public static class NawsModule
{
    [Transition(From = typeof(AwaitingOption), To = typeof(Naws)), On(NawsOption)]
    public static void Begin(in AwaitingOption from, ref SubNegotiation parent, ref Naws to) => parent.Option = NawsOption;

    [Transition(From = typeof(Naws)), OnAny]
    public static void Capture(ref Naws self, byte value)
    {
        self.Bytes ??= new byte[4];
        if (self.Index < 4)
        {
            self.Bytes[self.Index] = value;
            self.Index++;
        }
    }

    [Transition(From = typeof(Naws), To = typeof(NawsEscaping)), On(Iac)]
    public static void Escape(in Naws from, ref NawsEscaping to) => to.Captured = from;

    [Transition(From = typeof(NawsEscaping), To = typeof(Idle)), On(Se)]
    public static class Finish
    {
        public static bool Guard(in NawsEscaping from) => from.Captured.Index == 4;

        public static void Transform(in NawsEscaping from, ref Connected root)
        {
            var bytes = from.Captured.Bytes!;
            root.Width = (bytes[0] << 8) | bytes[1];
            root.Height = (bytes[2] << 8) | bytes[3];
        }

        public static void Completed(TelnetContext context, Connected root) => context.Log.Add($"window {root.Width}x{root.Height}");
    }
}
```
<sup><a href='/samples/StateAlchemist.Samples/Telnet/NawsModule.cs#L6-L48' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-naws-module' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Nothing in `TelnetCore` changes. The core never needed to know NAWS exists.

## 3. Let the application include it

```csharp
[Machine(Root = typeof(Connected), Value = typeof(byte), Context = typeof(TelnetContext))]
[Include(typeof(TelnetCore)), Include(typeof(NawsModule))]
public sealed partial class MudTelnet;
```

To save applications that line, offer the module from your assembly — `[assembly: ExportsModule(typeof(NawsModule))]`
— and a machine written with `[IncludeExported]` picks it up from the package reference. See
[modules a library offers](../concepts/machines.md#modules-a-library-offers).

## What the compiler checks for you

- If another module also claims `AwaitingOption` on byte 31, the application fails to compile with
  [`SALCH0101`](../reference/diagnostics.md#salch0101), naming both transitions.
- If `Begin` took `ref AwaitingOption`, it would be [`SALCH0201`](../reference/diagnostics.md#salch0201):
  `AwaitingOption` is left by that transition.
- If a module method is not `public static`, your library fails to compile with
  [`SALCH0002`](../reference/diagnostics.md#salch0002) — in your library, not in the application that includes it.

## Guidelines

- **Extend by adding transitions, never by editing another module's states.** A state's fields belong to the
  module that declares it.
- **Put shared data in the lowest common parent.** `SubNegotiation.Option` is readable by every subnegotiation
  module because it lives above all of them.
- **Prefer exact values to `[OnAny]`** in states other modules share: an `[OnAny]` there shadows everything the
  ancestors do with values, for every module.
