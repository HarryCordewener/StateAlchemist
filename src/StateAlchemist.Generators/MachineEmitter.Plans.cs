using System.Collections.Generic;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    private readonly SortedSet<(int Transition, int Leaf, int Refused)> _plans = [];
    private readonly SortedSet<int> _refusedNone = [];

    /// <summary>Each distinct list of refused guards a plan can carry, by its index: the names, in the order tried.</summary>
    private readonly List<string[]> _refused = [];

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

        for (var i = 0; i < _refused.Count; i++)
        {
            _w.Line();
            _w.Line($"private static readonly string[] s_refused{i} = new string[] {{ {string.Join(", ", _refused[i].Select(Literal))} }};");
        }

        foreach (var (index, leaf, refused) in _plans)
        {
            var transition = _model.Transitions[index];
            var paths = PathPlanner.Plans(_hierarchy, transition, leaf);
            string Field(TransitionPath path) => paths.Count == 1 ? PlanField(index, leaf, refused) : $"{PlanField(index, leaf, refused)}_Recall{path.TargetLeaf}";
            foreach (var path in paths)
            {
                _w.Line();
                _w.Line($"private static readonly {Rt}TransitionPlan {Field(path)} = new {Rt}TransitionPlan({Literal(transition.Name)}, typeof({S(path.Leaf)}), typeof({S(path.TargetLeaf)}), " +
                        $"{Rt}TransitionKind.{transition.Kind}, {Types(path.Exiting)}, {Types(path.Entering)}, {Bool(transition.IsDecision)}{(refused < 0 ? string.Empty : $", s_refused{refused}")});");
            }

            if (paths.Count > 1)
            {
                // A move by history: which plan depends on what was recorded.
                _w.Line();
                using (_w.Block($"private {Rt}TransitionPlan {PlanNow(index, leaf, refused)}()"))
                {
                    using (_w.Block($"switch ({RecallName(transition.Target, transition.History)}())"))
                    {
                        foreach (var path in paths.Skip(1))
                        {
                            _w.Line($"case StateId.{_stateIds[path.TargetLeaf]}: return {Field(path)};");
                        }

                        _w.Line($"default: return {Field(paths[0])};");
                    }
                }
            }
        }

        foreach (var refused in _refusedNone)
        {
            _w.Line();
            _w.Line($"private static readonly {Rt}TransitionPlan s_none{refused} = new {Rt}TransitionPlan(s_refused{refused});");
        }
    }

    /// <summary>
    /// Like dispatch, but a guard is called without its exception hook, and the answer is a plan. Guards are tried in
    /// order and each returns, so the guards refused before any candidate are known here: every plan is a static field.
    /// </summary>
    private void PlanCandidates(IReadOnlyList<TransitionModel> candidates, int leaf, string argument, string unhandled)
    {
        var tried = new List<string>();
        foreach (var candidate in candidates)
        {
            var refused = RefusedIndex(tried);
            _plans.Add((candidate.Index, leaf, refused));
            var plan = PathPlanner.Plans(_hierarchy, candidate, leaf).Count == 1
                ? PlanField(candidate.Index, leaf, refused)
                : $"{PlanNow(candidate.Index, leaf, refused)}()";
            if (candidate.Guard is { } guard)
            {
                var path = PathPlanner.Plan(_hierarchy, candidate, leaf);
                _w.Line($"if ({Owner(guard)}({Arguments(guard, Use.Guard, candidate, path, [])})) return {plan};");
                tried.Add(candidate.Name);
            }
            else
            {
                _w.Line($"return {plan};");
                return;
            }
        }

        var none = RefusedIndex(tried);
        if (none < 0)
        {
            _w.Line($"return {Rt}TransitionPlan.None;");
            return;
        }

        _refusedNone.Add(none);
        _w.Line($"return s_none{none};");
    }

    /// <summary>The index of <paramref name="names"/> among the refused lists, adding it if new; -1 for none.</summary>
    private int RefusedIndex(List<string> names)
    {
        if (names.Count == 0)
        {
            return -1;
        }

        var index = _refused.FindIndex(r => r.SequenceEqual(names));
        if (index >= 0)
        {
            return index;
        }

        _refused.Add(names.ToArray());
        return _refused.Count - 1;
    }

    private string PlanField(int transition, int leaf, int refused) =>
        $"s_plan{transition}_{_stateIds[leaf]}{(refused < 0 ? string.Empty : $"_r{refused}")}";

    /// <summary>For a move by history, the method that picks the plan by what was recorded.</summary>
    private string PlanNow(int transition, int leaf, int refused) => "Plan" + PlanField(transition, leaf, refused).Substring("s_plan".Length) + "_Now";

    private string Types(IReadOnlyList<int> states) =>
        states.Count == 0 ? $"new {TypeType}[0]" : $"new {TypeType}[] {{ {string.Join(", ", states.Select(s => $"typeof({S(s)})"))} }}";
}
