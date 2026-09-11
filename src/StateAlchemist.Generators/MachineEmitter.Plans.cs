using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    private readonly SortedSet<(int Transition, int Leaf)> _plans = [];

    /// <summary>The pure layer: what a trigger would do now, evaluating guards, doing nothing (docs/guides/testing.md).</summary>
    private void WritePlans()
    {
        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {Rt}TransitionPlan Plan({V} value)"))
        {
            _w.Line("var v = (int)value;");
            using (_w.Block("switch (_leaf)"))
            {
                foreach (var leaf in _hierarchy.Leaves)
                {
                    using (_w.Block($"case StateId.{_stateIds[leaf]}:"))
                    {
                        WriteValueCases(leaf, "value", PlanCandidates);
                    }
                }

                _w.Line($"default: return {Rt}TransitionPlan.None;");
            }
        }

        for (var i = 0; i < _events.Count; i++)
        {
            var eventName = SymbolModelBuilder.MetadataName(_events[i]);
            _w.Line();
            _w.Line($"/// <summary>What a <see cref=\"{Name(_events[i])}\"/> would do now.</summary>");
            using (_w.Block($"public {Rt}TransitionPlan Plan(in {Name(_events[i])} e)"))
            {
                using (_w.Block("switch (_leaf)"))
                {
                    foreach (var leaf in _hierarchy.Leaves)
                    {
                        var candidates = _resolver.ForEvent(leaf, eventName);
                        if (candidates.Count > 0)
                        {
                            using (_w.Block($"case StateId.{_stateIds[leaf]}:"))
                            {
                                PlanCandidates(candidates, leaf, "e", string.Empty);
                            }
                        }
                    }

                    _w.Line($"default: return {Rt}TransitionPlan.None;");
                }
            }
        }

        _w.Line();
        _w.Line("/// <inheritdoc/>");
        using (_w.Block($"public {Rt}TransitionPlan Plan<TEvent>(TEvent e) where TEvent : struct, {Rt}IEvent"))
        {
            for (var i = 0; i < _events.Count; i++)
            {
                _w.Line($"if (typeof(TEvent) == typeof({Name(_events[i])})) return Plan(in global::System.Runtime.CompilerServices.Unsafe.As<TEvent, {Name(_events[i])}>(ref e));");
            }

            _w.Line($"return {Rt}TransitionPlan.None;");
        }

        foreach (var (index, leaf) in _plans)
        {
            var transition = _model.Transitions[index];
            var path = PathPlanner.Plan(_hierarchy, transition, leaf);
            _w.Line();
            _w.Line($"private static readonly {Rt}TransitionPlan s_plan{index}_{_stateIds[leaf]} = new {Rt}TransitionPlan({Literal(transition.Name)}, typeof({S(path.Leaf)}), typeof({S(path.TargetLeaf)}), " +
                    $"{Rt}TransitionKind.{transition.Kind}, {Types(path.Exiting)}, {Types(path.Entering)}, {Bool(transition.IsDecision)});");
        }
    }

    /// <summary>Like dispatch, but a guard is called without its exception hook, and the answer is a plan.</summary>
    private void PlanCandidates(IReadOnlyList<TransitionModel> candidates, int leaf, string argument, string unhandled)
    {
        foreach (var candidate in candidates)
        {
            _plans.Add((candidate.Index, leaf));
            var plan = $"s_plan{candidate.Index}_{_stateIds[leaf]}";
            if (candidate.Guard is { } guard)
            {
                var path = PathPlanner.Plan(_hierarchy, candidate, leaf);
                _w.Line($"if ({Owner(guard)}({Arguments(guard, Use.Guard, candidate, path, [])})) return {plan};");
            }
            else
            {
                _w.Line($"return {plan};");
                return;
            }
        }

        _w.Line($"return {Rt}TransitionPlan.None;");
    }

    private string Types(IReadOnlyList<int> states) =>
        states.Count == 0 ? $"new {TypeType}[0]" : $"new {TypeType}[] {{ {string.Join(", ", states.Select(s => $"typeof({S(s)})"))} }}";
}
