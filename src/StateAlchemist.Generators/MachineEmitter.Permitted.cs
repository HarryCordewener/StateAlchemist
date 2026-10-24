using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    /// <summary>
    /// <c>PermittedTriggers</c>: per leaf, each run of values with the same candidates and each event, kept when some
    /// candidate would fire. Guards run as <c>Plan</c> runs them, without their exception hooks. A run whose guards do
    /// not read the value is decided once; one whose guards do is decided value by value.
    /// </summary>
    private void WritePermitted()
    {
        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public global::System.Collections.Generic.IReadOnlyList<{Rt}TriggerDefinition> PermittedTriggers()"))
        {
            _w.Line($"var permitted = new global::System.Collections.Generic.List<{Rt}TriggerDefinition>();");
            using (_w.Block("switch (_leaf)"))
            {
                foreach (var leaf in _hierarchy.Leaves)
                {
                    _w.Line($"case StateId.{_stateIds[leaf]}:");
                    using (_w.Block(string.Empty))
                    {
                        foreach (var run in ValueRuns(leaf).Where(r => r.Candidates.Count > 0))
                        {
                            WritePermittedValues(leaf, run.Low, run.High, run.Candidates);
                        }

                        for (var i = 0; i < _events.Count; i++)
                        {
                            var candidates = _resolver.ForEvent(leaf, SymbolModelBuilder.MetadataName(_events[i]));
                            if (candidates.Count == 0 || IsInternal(i))
                            {
                                continue;
                            }

                            var add = $"permitted.Add({Rt}TriggerDefinition.ForEvent(typeof({Name(_events[i])})));";
                            var test = PermitTest(candidates, leaf, readsTrigger: p => p.Kind is ParameterKind.Event or ParameterKind.TransitionInfo);
                            _w.Line(test is null ? add : $"if ({test}) {add}");
                        }

                        _w.Line("break;");
                    }
                }
            }

            _w.Line("return permitted;");
        }

        _w.Line();
        _w.Line("/// <summary>Adds <paramref name=\"low\"/>..<paramref name=\"high\"/>, joined to the last trigger when they are neighbours.</summary>");
        using (_w.Block($"private static void Permit(global::System.Collections.Generic.List<{Rt}TriggerDefinition> permitted, long low, long high)"))
        {
            using (_w.Block("if (permitted.Count > 0)"))
            {
                _w.Line("var last = permitted[permitted.Count - 1];");
                _w.Line($"if ((last.Kind == {Rt}TriggerKind.Value || last.Kind == {Rt}TriggerKind.Range) && last.High == low - 1) {{ permitted[permitted.Count - 1] = {Rt}TriggerDefinition.ForRange(last.Low, high); return; }}");
            }

            _w.Line($"permitted.Add(low == high ? {Rt}TriggerDefinition.ForValue(low) : {Rt}TriggerDefinition.ForRange(low, high));");
        }
    }

    private void WritePermittedValues(int leaf, long low, long high, IReadOnlyList<TransitionModel> candidates)
    {
        var whole = $"Permit(permitted, {low}, {high});";
        var once = PermitTest(candidates, leaf, readsTrigger: _ => false);
        if (once is null)
        {
            _w.Line(whole);
            return;
        }

        var readsValue = candidates.Any(c => c.Guard is { } g && g.Parameters.Any(p => p.Kind is ParameterKind.Value or ParameterKind.TransitionInfo));
        if (!readsValue)
        {
            _w.Line($"if ({once}) {whole}");
            return;
        }

        using (_w.Block($"for (var x = {low}; x <= {high}; x++)"))
        {
            _w.Line($"var value = ({V})x;");
            _w.Line($"if ({once}) Permit(permitted, x, x);");
        }
    }

    /// <summary>
    /// The condition under which one of <paramref name="candidates"/> would fire, or <see langword="null"/> when one
    /// always would: an unguarded candidate, or a guard that reads what only the trigger itself can tell.
    /// </summary>
    private string? PermitTest(IReadOnlyList<TransitionModel> candidates, int leaf, System.Func<ParameterModel, bool> readsTrigger)
    {
        var tests = new List<string>();
        foreach (var candidate in candidates)
        {
            if (candidate.Guard is not { } guard || guard.Parameters.Any(readsTrigger))
            {
                return null;
            }

            var path = PathPlanner.Plan(_hierarchy, candidate, leaf);
            tests.Add($"{Owner(guard)}({Arguments(guard, Use.Guard, candidate, path, [])})");
        }

        return string.Join(" || ", tests);
    }
}
