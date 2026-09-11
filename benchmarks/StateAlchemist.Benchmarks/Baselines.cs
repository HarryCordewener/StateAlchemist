using System;
using System.Threading.Tasks;
using Stateless;
using StateAlchemist.Samples.Performance;

namespace StateAlchemist.Benchmarks;

/// <summary>
/// The performance sample written by hand: the fastest a machine of this shape can be — a switch on the state, then
/// on the value — with the same data, the same actions and the same results. Spec §9 holds the generated machine to
/// within twice this.
/// </summary>
public sealed class HandWrittenMachine(Counters counters)
{
    private enum Leaf : byte { Text, Parked }

    private Leaf _leaf;
    private Root _root;
    private Text _text;

    public int Count => _text.Count;

    public int Length => _text.Length;

    public ValueTask FireAsync(byte value)
    {
        switch (_leaf)
        {
            case Leaf.Text:
                switch (value)
                {
                    case 1: _text.Count++; return default;
                    case 2:
                        _root.Moves++;
                        _leaf = Leaf.Parked;
                        counters.Exits++;
                        counters.Exits++;
                        counters.Entries++;
                        counters.Entries++;
                        _text = default;
                        return default;
                    case PerformanceModule.Iac: _text.Count++; return default;
                    default: _text.Length++; return default;
                }

            default:
                if (value == 3)
                {
                    _root.Moves++;
                    _leaf = Leaf.Text;
                }

                return default;
        }
    }

    public ValueTask FireAsync(ReadOnlyMemory<byte> values)
    {
        var span = values.Span;
        for (var i = 0; i < span.Length;)
        {
            if (_leaf == Leaf.Text && span[i] is not (1 or 2 or 4 or PerformanceModule.Iac))
            {
                var stop = span.Slice(i).IndexOfAny((byte)1, (byte)2, PerformanceModule.Iac);
                var length = stop < 0 ? span.Length - i : stop;
                _text.Length += length;
                i += length;
                continue;
            }

            _ = FireAsync(span[i++]);
        }

        return default;
    }
}

/// <summary>The same shape in Stateless 5.20, fired synchronously — what TNC runs today.</summary>
public sealed class StatelessMachine
{
    private enum State { Root, Outer, Text, Other, Parked }

    private readonly StateMachine<State, byte> _machine = new(State.Text);
    private Root _root;
    private Text _text;

    public StatelessMachine(Counters counters)
    {
        _machine.Configure(State.Outer).SubstateOf(State.Root).OnExit(() => counters.Exits++);
        var text = _machine.Configure(State.Text).SubstateOf(State.Outer)
            .InternalTransition(1, () => _text.Count++)
            .Permit(2, State.Parked)
            .InternalTransition(PerformanceModule.Iac, () => _text.Count++)
            .OnExit(() => { counters.Exits++; _text = default; });
        var other = _machine.Configure(State.Other).SubstateOf(State.Root).OnEntry(() => counters.Entries++);
        _machine.Configure(State.Parked).SubstateOf(State.Other)
            .OnEntry(() => { counters.Entries++; _root.Moves++; })
            .Permit(3, State.Text);

        // Stateless has no "any value" trigger and no runs: text is one internal transition per value, and the
        // parked state ignores every value but the one that resumes.
        for (var value = 0; value < 256; value++)
        {
            if (value is not (1 or 2 or 4 or PerformanceModule.Iac))
            {
                text.InternalTransition((byte)value, () => _text.Length++);
            }

            if (value != 3)
            {
                other.Ignore((byte)value);
            }
        }
    }

    public int Count => _text.Count;

    public int Length => _text.Length;

    public void Fire(byte value) => _machine.Fire(value);
}
