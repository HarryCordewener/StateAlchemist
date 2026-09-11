# StateAlchemist

A source-generated hierarchical state machine library for .NET. Your states own their data, your transitions own
the transformation, and the compiler writes the machine — no dictionaries, no delegates, no reflection, and nothing
allocated on a synchronous path.

```csharp
public struct Connected : IRootState { public int Width; public int Height; }
[Initial] public struct Idle : IState<Connected> { }

[Module] public static class TelnetCore
{
    [Transition(From = typeof(Idle), To = typeof(Command)), On(255)]
    public static void Escape(ref Command to) { }
}

[Machine(Root = typeof(Connected), Value = typeof(byte))]
[Include(typeof(TelnetCore))]
public sealed partial class MudTelnet;
```

`await machine.StartAsync();` then `await machine.FireAsync(buffer);` — the generator writes the dispatch, the
hierarchy, the entry and exit actions, the diagnostics that catch mistakes at compile time, and a Mermaid diagram
of the whole thing as a constant.

Referencing this package brings the generator, the analyzers and their code fixes with it.

Documentation: https://github.com/HarryCordewener/StateAlchemist
