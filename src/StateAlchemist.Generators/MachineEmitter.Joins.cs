using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Generators;

internal sealed partial class MachineEmitter
{
    private readonly SortedSet<(int Transition, int Leaf)> _arrivals = [];
    private List<Join>? _joins;

    /// <summary>The machine's joins, one slot each, in the order they are declared.</summary>
    private List<Join> Joins => _joins ??= _model.Transitions
        .Where(t => t.IsJoin)
        .GroupBy(t => t.Name)
        .Select((g, slot) => new Join(slot, g.Key, g.First().Source, g.First().Join!))
        .ToList();

    private Join JoinOf(TransitionModel transition) => Joins.First(j => j.Name == transition.Name);

    /// <summary>
    /// Each join's slot: the arrival mask and the latest payload of every listed event, in a struct so that clearing
    /// it, and snapshotting it for a state started over, is one assignment. Nothing is boxed.
    /// </summary>
    private void WriteJoinStorage()
    {
        foreach (var join in Joins)
        {
            _w.Line();
            _w.Line($"/// <summary>What <c>{join.Name}</c> has received while <see cref=\"{S(join.Source)}\"/> is active.</summary>");
            using (_w.Block($"private struct {JoinType(join)}"))
            {
                _w.Line("public uint Mask;");
                for (var i = 0; i < join.Model.Events.Count; i++)
                {
                    _w.Line($"public {Event(join.Model.Events[i])} E{i};");
                }
            }

            _w.Line($"private {JoinType(join)} {JoinField(join)};");
        }
    }

    /// <summary>Clears the slots of the joins whose source is <paramref name="state"/>: they live as long as its data.</summary>
    private void ClearJoins(int state)
    {
        foreach (var join in Joins.Where(j => j.Source == state))
        {
            _w.Line($"{JoinField(join)} = default({JoinType(join)});");
        }
    }

    private IEnumerable<Join> JoinsOf(IEnumerable<int> states) => Joins.Where(j => states.Contains(j.Source));

    /// <summary>
    /// One event's arrival at a join from one leaf: record it, and fire the join once every event has arrived. The
    /// transition takes a copy of the slot, which is cleared first, so a state the join exits or starts over can be
    /// cleared without losing what the transform reads.
    /// </summary>
    private void WriteArrivals()
    {
        foreach (var (index, leaf) in _arrivals)
        {
            var transition = _model.Transitions[index];
            var join = JoinOf(transition);
            var bit = join.Model.BitOf(transition.Trigger.EventType!);
            var field = JoinField(join);
            _w.Line();
            using (_w.Block($"private {ValueTaskType} {ArrivalName(index, leaf)}({Event(transition.Trigger.EventType!)} e)"))
            {
                _w.Line($"{field}.E{bit} = e;");
                _w.Line($"{field}.Mask |= {Mask(1u << bit)};");
                _w.Line($"if ({field}.Mask != {Mask(join.Model.Full)}) return default({ValueTaskType});");
                _w.Line($"var join = {field};");
                _w.Line($"{field} = default({JoinType(join)});");
                _w.Line($"return {TransitionName(index, leaf)}(join);");
            }
        }
    }

    /// <summary>What a join's plan says for one event: the join if the event completes it, otherwise only an arrival.</summary>
    private string JoinPlan(TransitionModel candidate, int leaf, string plan, int refused)
    {
        var join = JoinOf(candidate);
        _joinPlans.Add((candidate.Index, leaf, refused));
        var bit = join.Model.BitOf(candidate.Trigger.EventType!);
        return $"({JoinField(join)}.Mask | {Mask(1u << bit)}) == {Mask(join.Model.Full)} ? {plan} : {JoinPlanField(candidate.Index, leaf, refused)}";
    }

    private readonly SortedSet<(int Transition, int Leaf, int Refused)> _joinPlans = [];

    private void WriteJoinPlans()
    {
        foreach (var (index, leaf, refused) in _joinPlans)
        {
            var transition = _model.Transitions[index];
            _w.Line();
            _w.Line($"private static readonly {Rt}TransitionPlan {JoinPlanField(index, leaf, refused)} = {Rt}TransitionPlan.ForJoinArrival({Literal(transition.Name)}, typeof({S(leaf)}), " +
                    $"{(refused < 0 ? "new string[0]" : $"s_refused{refused}")});");
        }
    }

    private string JoinPlanField(int transition, int leaf, int refused) =>
        $"s_arrival{transition}_{_stateIds[leaf]}{(refused < 0 ? string.Empty : $"_r{refused}")}";

    /// <summary>The argument for an event parameter of a join's method: the payload recorded for that event.</summary>
    private string JoinArgument(TransitionModel transition, ParameterModel parameter) =>
        "join.E" + JoinOf(transition).Model.BitOf(parameter.TypeName).ToString(CultureInfo.InvariantCulture);

    private string ArrivalName(int transition, int leaf) => $"J{transition}_{_stateIds[leaf]}";

    private static string JoinType(Join join) => "Join" + join.Slot.ToString(CultureInfo.InvariantCulture);

    private static string JoinField(Join join) => "_j" + join.Slot.ToString(CultureInfo.InvariantCulture);

    private static string Mask(uint mask) => "0x" + mask.ToString("X", CultureInfo.InvariantCulture) + "u";

    private sealed record Join(int Slot, string Name, int Source, JoinModel Model);
}
