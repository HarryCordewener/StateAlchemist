using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    private readonly SortedSet<(int Transition, int Leaf)> _decisions = [];

    /// <summary>
    /// Decisions (spec §5.6, §6.5). A synchronous <c>Decide</c> runs inline; a <c>DecideAsync</c> enters the pending
    /// state and runs apart, and its result is applied by the pump. Either way each outcome is a move from the leaf,
    /// written as its own method with the outcome's <c>Complete</c> as the transform.
    /// </summary>
    private void WriteDecisions()
    {
        foreach (var (index, leaf) in _decisions)
        {
            var decision = _model.Transitions[index];
            WriteDecide(decision, leaf);
            WriteOutcomes(decision, leaf);
        }

        foreach (var index in _decisions.Select(d => d.Transition).Distinct().Where(i => _model.Transitions[i].Decision!.DecideAsync is not null))
        {
            WriteRunDecision(_model.Transitions[index]);
        }

        if (_model.Transitions.Any(t => t.Decision?.DecideAsync is not null))
        {
            WriteApplyDecision();
        }
    }

    private void WriteDecide(TransitionModel decision, int leaf)
    {
        var parts = decision.Decision!;
        var argument = TriggerArgument(decision);
        _w.Line();
        _w.Line($"// {decision.Name}: decided in {_model.States[leaf].Name}");
        using (_w.Block($"private {ValueTaskType} {DecisionName(decision.Index, leaf)}({TriggerParameter(decision)})"))
        {
            if (parts.DecideAsync is not null)
            {
                _w.Line("Pending pending;");
                using (_w.Block("lock (_sync)"))
                {
                    _w.Line($"if (_pending != null) throw new global::System.InvalidOperationException(\"'{decision.Name}' cannot start while decision '\" + _pending.Name + \"' is pending.\");");
                    _w.Line($"pending = new Pending {{ Decision = {decision.Index}, Name = {Literal(decision.Name)}, Owner = _owner }};");
                    _w.Line(decision.Trigger.Kind == MatchKind.Event ? "pending.Event = e;" : "pending.Value = value;");
                    _w.Line("_pending = pending;");
                    _w.Line("_started = pending;");
                }

                _w.Line($"_ = RunDecision{decision.Index}(pending);");
                _w.Line($"return default({ValueTaskType});");
                return;
            }

            var path = PathPlanner.Stay(leaf);
            _w.Line("object outcome;");
            using (_w.Block("try"))
            {
                _w.Line($"outcome = {Owner(parts.Decide!)}({Arguments(parts.Decide!, Use.Decide, decision, path, [])}).Value;");
                _w.Line($"if (outcome == null) throw new global::System.InvalidOperationException({Literal($"'{decision.Name}' returned no outcome.")});");
            }

            using (_w.Block($"catch ({Exception} exception)"))
            {
                _w.Line($"return DispatchEvent{DecisionFailedTag}(new {Rt}DecisionFailed({Literal(decision.Name)}, exception));");
            }

            WriteOutcomeSwitch(decision, leaf, "outcome", argument);
        }
    }

    /// <summary>Picks the outcome's move by the type of the case the union holds.</summary>
    private void WriteOutcomeSwitch(TransitionModel decision, int leaf, string outcome, string argument)
    {
        var completions = decision.Decision!.Completions;
        for (var i = 0; i < completions.Count; i++)
        {
            var type = Name(OutcomeType(completions[i]));
            _w.Line($"if ({outcome} is {type}) return {OutcomeName(decision.Index, i, leaf)}({argument}, ({type}){outcome});");
        }

        _w.Line($"throw new global::System.InvalidOperationException({Literal($"'{decision.Name}' returned an outcome it does not complete.")});");
    }

    private void WriteOutcomes(TransitionModel decision, int leaf)
    {
        var completions = decision.Decision!.Completions;
        for (var i = 0; i < completions.Count; i++)
        {
            var completion = completions[i];
            var type = OutcomeType(completion);
            var completed = decision.Completed
                .Where(m => m.Parameters.FirstOrDefault(p => p.Kind == ParameterKind.Outcome) is not { } taken || taken.TypeName == completion.OutcomeType)
                .ToList();
            var kind = completion.Target == decision.Source ? "Reenter" : "Move";
            WriteSteps(OutcomeName(decision.Index, i, leaf), $"{TriggerParameter(decision)}, {Name(type)} outcome", decision,
                PathPlanner.Move(_hierarchy, leaf, completion.Target), completion.Complete, completed, kind, outcome: Name(type));
        }
    }

    /// <summary>
    /// Runs a <c>DecideAsync</c> apart from the pump, marked so it cannot fire its own machine (spec §6.6), and records
    /// its result only if the decision is still the pending one.
    /// </summary>
    private void WriteRunDecision(TransitionModel decision)
    {
        var decide = decision.Decision!.DecideAsync!;
        _w.Line();
        using (_w.Block($"private async global::System.Threading.Tasks.Task RunDecision{decision.Index}(Pending pending)"))
        {
            _w.Line("_flow.Value = pending;");
            _w.Line(decision.Trigger.Kind == MatchKind.Event ? $"var e = ({Event(decision.Trigger.EventType!)})pending.Event;" : "var value = pending.Value;");
            _w.Line("object outcome = null;");
            _w.Line($"{Exception} failure = null;");
            using (_w.Block("try"))
            {
                _w.Line($"outcome = (await {Owner(decide)}({Arguments(decide, Use.Decide, decision, PathPlanner.Stay(decision.Source), [])})).Value;");
                _w.Line($"if (outcome == null) throw new global::System.InvalidOperationException({Literal($"'{decision.Name}' returned no outcome.")});");
            }

            _w.Line($"catch ({Exception} exception) {{ failure = exception; }}");
            _w.Line("_flow.Value = null;");
            using (_w.Block("lock (_sync)"))
            {
                _w.Line($"if (_pending != pending || _status != {Rt}MachineStatus.Running) return;");
                _w.Line("pending.Outcome = outcome;");
                _w.Line("pending.Failure = failure;");
                _w.Line("pending.HasResult = true;");
            }

            _w.Line("PumpNow();");
        }
    }

    /// <summary>A pending decision's result: its failure fires <c>DecisionFailed</c>; its outcome runs that outcome's move.</summary>
    private void WriteApplyDecision()
    {
        _w.Line();
        using (_w.Block($"private {ValueTaskType} ApplyDecision(Pending pending)"))
        {
            using (_w.Block("switch (pending.Decision)"))
            {
                foreach (var group in _decisions.Where(d => _model.Transitions[d.Transition].Decision!.DecideAsync is not null).GroupBy(d => d.Transition))
                {
                    var decision = _model.Transitions[group.Key];
                    using (_w.Block($"case {decision.Index}:"))
                    {
                        _w.Line($"if (pending.Failure != null) return DispatchEvent{DecisionFailedTag}(new {Rt}DecisionFailed({Literal(decision.Name)}, pending.Failure));");
                        _w.Line(decision.Trigger.Kind == MatchKind.Event ? $"var e = ({Event(decision.Trigger.EventType!)})pending.Event;" : "var value = pending.Value;");
                        using (_w.Block("switch (_leaf)"))
                        {
                            foreach (var (_, leaf) in group)
                            {
                                using (_w.Block($"case StateId.{_stateIds[leaf]}:"))
                                {
                                    WriteOutcomeSwitch(decision, leaf, "pending.Outcome", TriggerArgument(decision));
                                }
                            }

                            _w.Line($"default: return default({ValueTaskType});");
                        }
                    }
                }

                _w.Line($"default: return default({ValueTaskType});");
            }
        }

        _w.Line();
        _w.Line("/// <summary>Whether the pending decision lists the event in <c>Handle</c>: it runs at once instead of waiting.</summary>");
        using (_w.Block("private static bool Handles(int decision, int tag)"))
        {
            using (_w.Block("switch (decision)"))
            {
                foreach (var decision in _model.Transitions.Where(t => t.Decision?.DecideAsync is not null))
                {
                    var tags = decision.Decision!.Handle.Select(EventTag).Where(t => t >= 0).ToList();
                    _w.Line($"case {decision.Index}: return {(tags.Count == 0 ? "false" : string.Join(" || ", tags.Select(t => $"tag == {t}")))};");
                }

                _w.Line("default: return false;");
            }
        }
    }

    private ITypeSymbol OutcomeType(OutcomeCompletion completion) =>
        _machine.Methods[completion.Complete].Parameters.First(p => SymbolModelBuilder.MetadataName(p.Type) == completion.OutcomeType).Type;

    private int DecisionFailedTag => EventTag("StateAlchemist.DecisionFailed");

    private static string TriggerArgument(TransitionModel transition) => transition.Trigger.Kind == MatchKind.Event ? "e" : "value";

    private string OutcomeName(int decision, int outcome, int leaf) => $"C{decision}_{outcome}_{_stateIds[leaf]}";
}
