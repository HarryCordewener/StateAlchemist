using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    /// <summary>
    /// Runs (spec §6.7). A value belongs to a leaf's run when resolution tries the run transition first for it — the
    /// stop set's complement — so <c>RunOf</c> is the dispatch <c>switch</c> answering "which run, if any", and a
    /// batch hands everything up to the first value with a different answer to one call.
    /// </summary>
    private void WriteRuns()
    {
        if (!HasRuns)
        {
            return;
        }

        var runs = new List<(int Leaf, int Transition)>();
        _w.Line();
        _w.Line("/// <summary>The run transition the active leaf gives <paramref name=\"value\"/> to first, or −1.</summary>");
        using (_w.Block($"private int RunOf({V} value)"))
        {
            _w.Line("var v = (int)value;");
            using (_w.Block("switch (_leaf)"))
            {
                foreach (var leaf in _hierarchy.Leaves)
                {
                    var first = _model.Options.Domain.Values.Select(value => _resolver.ForValue(leaf, value)).Where(c => c.Count > 0 && c[0].IsRun).Select(c => c[0].Index).Distinct().ToList();
                    if (first.Count == 0)
                    {
                        continue;
                    }

                    runs.AddRange(first.Select(t => (leaf, t)));
                    using (_w.Block($"case StateId.{_stateIds[leaf]}:"))
                    {
                        WriteValueCases(leaf, "value", (candidates, _, _, _) => _w.Line($"return {(candidates.Count > 0 && candidates[0].IsRun ? candidates[0].Index : -1)};"));
                    }
                }

                _w.Line("default: return -1;");
            }
        }

        _w.Line();
        _w.Line("/// <summary>How many values from the start of <paramref name=\"values\"/> go to one call: a whole run, or one value.</summary>");
        using (_w.Block($"private int RunLength(global::System.ReadOnlyMemory<{V}> values)"))
        {
            _w.Line("var span = values.Span;");
            _w.Line("var run = RunOf(span[0]);");
            _w.Line("if (run < 0) return 1;");
            _w.Line("var count = 1;");
            _w.Line("while (count < span.Length && RunOf(span[count]) == run) count++;");
            _w.Line("return count;");
        }

        _w.Line();
        using (_w.Block($"private {ValueTaskType} DispatchRun(global::System.ReadOnlyMemory<{V}> run)"))
        {
            using (_w.Block("switch (RunOf(run.Span[0]))"))
            {
                foreach (var group in runs.GroupBy(r => r.Transition))
                {
                    using (_w.Block($"case {group.Key}:"))
                    {
                        using (_w.Block("switch (_leaf)"))
                        {
                            foreach (var (leaf, transition) in group)
                            {
                                _transitions.Add((transition, leaf));
                                _w.Line($"case StateId.{_stateIds[leaf]}: return {TransitionName(transition, leaf)}(run);");
                            }
                        }

                        _w.Line("break;");
                    }
                }
            }

            _w.Line("return DispatchValue(run.Span[0]);");
        }

        _w.Line();
        _w.Line("/// <summary>A single value as a run of one. The buffer is reused: one trigger runs at a time.</summary>");
        _w.Line($"private global::System.ReadOnlyMemory<{V}> One({V} value) {{ _one[0] = value; return _one; }}");
    }
}
