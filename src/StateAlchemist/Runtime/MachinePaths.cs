using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StateAlchemist;

/// <summary>
/// Test paths computed from a <see cref="MachineDefinition"/>: for every transition, and every outcome of a decision,
/// the shortest sequence of triggers from the start that fires it. A test suite that drives a machine along each path
/// fires every transition at least once.
/// </summary>
/// <remarks>
/// <para>
/// The paths follow the machine's own resolution: from the active leaf to the root, values before ranges before
/// <c>[OnAny]</c>, guarded candidates before the unguarded one. They assume that guards go the way the path needs:
/// <see cref="MachineStep.AssumesGuards"/> marks each step that relies on a guard passing, or on an earlier guard
/// refusing. A move by history is followed as if nothing had been recorded: to the target's <c>[Initial]</c> leaf.
/// </para>
/// <para>
/// Values are chosen from the machine's own trigger bounds, so no value type is enumerated: the computation is
/// trimming- and AOT-safe. Eventless and done transitions appear as steps the machine takes on its own.
/// </para>
/// </remarks>
public static class MachinePaths
{
    /// <summary>
    /// One path per transition and decision outcome, shortest first, leaving out a path that is the start of a longer
    /// one in the list.
    /// </summary>
    /// <param name="definition">The machine, as its generated <c>Definition</c> describes it.</param>
    public static IReadOnlyList<MachinePath> Covering(MachineDefinition definition)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        var graph = new Graph(definition);
        var start = graph.InitialLeaf(definition.Root.Index);
        var reached = new Dictionary<int, List<MachineStep>> { [start] = [] };
        var pending = new Queue<int>();
        pending.Enqueue(start);
        var covering = new Dictionary<string, List<MachineStep>>();
        while (pending.Count > 0)
        {
            var leaf = pending.Dequeue();
            foreach (var step in graph.Steps(leaf))
            {
                var path = new List<MachineStep>(reached[leaf]) { step };
                var key = step.Transition.Name + "/" + (step.Outcome?.Type.FullName ?? string.Empty);
                if (!covering.ContainsKey(key))
                {
                    covering[key] = path;
                }

                var next = definition.IndexOf(step.To);
                if (!reached.ContainsKey(next))
                {
                    reached[next] = path;
                    pending.Enqueue(next);
                }
            }
        }

        var paths = covering.Values
            .OrderBy(p => p.Count)
            .ThenBy(p => string.Join(" ", p.Select(s => s.ToString())), StringComparer.Ordinal)
            .ToList();
        return paths
            .Where(p => !paths.Any(longer => longer.Count > p.Count && longer.Take(p.Count).SequenceEqual(p)))
            .Select(p => new MachinePath(p))
            .ToList();
    }

    /// <summary>The definition as a tree, with the resolution the generated machine performs.</summary>
    private sealed class Graph
    {
        private readonly MachineDefinition _definition;
        private readonly List<int>[] _children;
        private readonly long _min;
        private readonly long _max;

        public Graph(MachineDefinition definition)
        {
            _definition = definition;
            _children = definition.States.Select(_ => new List<int>()).ToArray();
            foreach (var state in definition.States.Where(s => !s.IsRoot))
            {
                _children[state.Parent].Add(state.Index);
            }

            (_min, _max) = Bounds(definition.ValueType);
        }

        public int InitialLeaf(int state)
        {
            while (_children[state].Count > 0)
            {
                var children = _children[state];
                state = children.Where(c => _definition.States[c].IsInitial).DefaultIfEmpty(children[0]).First();
            }

            return state;
        }

        /// <summary>Every step the machine can take from <paramref name="leaf"/>, assuming guards go either way.</summary>
        public IEnumerable<MachineStep> Steps(int leaf)
        {
            var always = Candidates(leaf, t => t.Trigger.Kind == TriggerKind.Always, ancestors: true);
            if (always.Count > 0)
            {
                foreach (var step in StepsFor(leaf, always, t => t.Trigger))
                {
                    yield return step;
                }

                if (!always[always.Count - 1].HasGuard)
                {
                    yield break; // the machine never rests here
                }
            }

            var state = _definition.States[leaf];
            if (state.IsFinal)
            {
                if (_definition.States[state.Parent].IsRoot)
                {
                    yield break; // completed
                }

                var done = Candidates(state.Parent, t => t.Trigger.Kind == TriggerKind.Done, ancestors: false);
                foreach (var step in StepsFor(leaf, done, t => t.Trigger))
                {
                    yield return step;
                }

                yield break;
            }

            foreach (var value in Values())
            {
                var candidates = ValueCandidates(leaf, value);
                foreach (var step in StepsFor(leaf, candidates, _ => TriggerDefinition.ForValue(value)))
                {
                    yield return step;
                }
            }

            foreach (var eventType in _definition.Transitions.Where(t => t.Trigger.Kind == TriggerKind.Event).Select(t => t.Trigger.EventType!).Distinct().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                var candidates = Candidates(leaf, t => t.Trigger.Kind == TriggerKind.Event && t.Trigger.EventType == eventType, ancestors: true);
                foreach (var step in StepsFor(leaf, candidates, t => t.Trigger))
                {
                    yield return step;
                }
            }

            foreach (var group in _definition.Transitions.Where(t => t.Trigger.Kind == TriggerKind.Timer && IsAncestorOrSelf(t.Source, leaf)).GroupBy(t => (t.Source, t.Trigger)))
            {
                foreach (var step in StepsFor(leaf, Ordered(group.ToList()), t => t.Trigger))
                {
                    yield return step;
                }
            }
        }

        /// <summary>Each candidate a trigger may fire, by guards: one step per candidate, or per outcome of a decision.</summary>
        private IEnumerable<MachineStep> StepsFor(int leaf, IReadOnlyList<TransitionDefinition> candidates, Func<TransitionDefinition, TriggerDefinition> trigger)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                var transition = candidates[i];
                var assumes = transition.HasGuard || i > 0;
                if (transition.IsDecision && transition.Outcomes.Count > 0)
                {
                    foreach (var outcome in transition.Outcomes)
                    {
                        yield return new MachineStep(transition, trigger(transition), outcome, _definition.States[leaf].Type, _definition.States[InitialLeaf(outcome.Target)].Type, assumes: true);
                    }

                    continue;
                }

                var target = transition.Kind == TransitionKind.Stay || transition.IsDecision ? leaf : InitialLeaf(transition.Target);
                yield return new MachineStep(transition, trigger(transition), null, _definition.States[leaf].Type, _definition.States[target].Type, assumes);
            }
        }

        /// <summary>The candidates for a value: per level, exact values, then ranges, then <c>[OnAny]</c>.</summary>
        private List<TransitionDefinition> ValueCandidates(int leaf, long value)
        {
            var candidates = new List<TransitionDefinition>();
            for (var state = leaf; state >= 0; state = _definition.States[state].Parent)
            {
                var level = _definition.Transitions.Where(t => t.Source == state).ToList();
                if (Add(candidates, level.Where(t => t.Trigger.Kind == TriggerKind.Value && t.Trigger.Low == value).ToList())
                    || Add(candidates, level.Where(t => t.Trigger.Kind == TriggerKind.Range && t.Trigger.Matches(value)).ToList())
                    || Add(candidates, level.Where(t => t.Trigger.Kind == TriggerKind.Any).ToList()))
                {
                    break;
                }
            }

            return candidates;
        }

        private List<TransitionDefinition> Candidates(int leaf, Func<TransitionDefinition, bool> matches, bool ancestors)
        {
            var candidates = new List<TransitionDefinition>();
            for (var state = leaf; state >= 0; state = ancestors ? _definition.States[state].Parent : -1)
            {
                if (Add(candidates, _definition.Transitions.Where(t => t.Source == state && matches(t)).ToList()))
                {
                    break;
                }
            }

            return candidates;
        }

        /// <summary>Adds a level's matches, guarded by order then the unguarded one; true when an unguarded one ends the list.</summary>
        private static bool Add(List<TransitionDefinition> candidates, List<TransitionDefinition> level)
        {
            var ordered = Ordered(level);
            candidates.AddRange(ordered);
            return ordered.Count > 0 && !ordered[ordered.Count - 1].HasGuard;
        }

        private static List<TransitionDefinition> Ordered(List<TransitionDefinition> level)
        {
            // A join is one definition per event it waits for: one candidate.
            var distinct = level.GroupBy(t => t.Name).Select(g => g.First()).ToList();
            var ordered = distinct.Where(t => t.HasGuard).OrderBy(t => t.Order).ThenBy(t => t.Index).ToList();
            if (distinct.FirstOrDefault(t => !t.HasGuard) is { } unguarded)
            {
                ordered.Add(unguarded);
            }

            return ordered;
        }

        /// <summary>Values that tell the machine's value triggers apart: each bound, and either side of it.</summary>
        private IEnumerable<long> Values()
        {
            var values = new SortedSet<long>();
            foreach (var trigger in _definition.Transitions.Select(t => t.Trigger).Where(t => t.Kind is TriggerKind.Value or TriggerKind.Range or TriggerKind.Any))
            {
                if (trigger.Kind == TriggerKind.Any)
                {
                    values.Add(_min);
                    values.Add(_max);
                    continue;
                }

                foreach (var bound in new[] { trigger.Low - 1, trigger.Low, trigger.High, trigger.High + 1 })
                {
                    if (bound >= _min && bound <= _max)
                    {
                        values.Add(bound);
                    }
                }
            }

            return values;
        }

        private bool IsAncestorOrSelf(int ancestor, int state)
        {
            for (; state >= 0; state = _definition.States[state].Parent)
            {
                if (state == ancestor)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The value type's range, read from its type code: an enum reports its underlying type's.</summary>
        private static (long Min, long Max) Bounds(Type valueType) => Type.GetTypeCode(valueType) switch
        {
            TypeCode.Byte => (byte.MinValue, byte.MaxValue),
            TypeCode.SByte => (sbyte.MinValue, sbyte.MaxValue),
            TypeCode.Int16 => (short.MinValue, short.MaxValue),
            TypeCode.UInt16 => (ushort.MinValue, ushort.MaxValue),
            TypeCode.Char => (char.MinValue, char.MaxValue),
            TypeCode.Int32 => (int.MinValue, int.MaxValue),
            TypeCode.UInt32 => (uint.MinValue, uint.MaxValue),
            _ => (long.MinValue, long.MaxValue),
        };
    }
}

/// <summary>A sequence of steps from the start, ending in a transition that only it covers.</summary>
public sealed class MachinePath
{
    internal MachinePath(IReadOnlyList<MachineStep> steps) => Steps = steps;

    /// <summary>The steps, in order.</summary>
    public IReadOnlyList<MachineStep> Steps { get; }

    /// <summary>The leaf the path ends in.</summary>
    public Type End => Steps[Steps.Count - 1].To;

    /// <summary>The steps, one per line.</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var step in Steps)
        {
            text.AppendLine(step.ToString());
        }

        return text.ToString();
    }
}

/// <summary>One step of a <see cref="MachinePath"/>: a trigger and the transition it fires.</summary>
public sealed class MachineStep
{
    internal MachineStep(TransitionDefinition transition, TriggerDefinition trigger, OutcomeDefinition? outcome, Type from, Type to, bool assumes)
    {
        Transition = transition;
        Trigger = trigger;
        Outcome = outcome;
        From = from;
        To = to;
        AssumesGuards = assumes;
    }

    /// <summary>The transition the step fires.</summary>
    public TransitionDefinition Transition { get; }

    /// <summary>
    /// What to fire: a value (a range or <c>[OnAny]</c> resolved to one value), an event type, a timer, or, for an
    /// eventless or done transition, nothing — the machine takes the step on its own.
    /// </summary>
    public TriggerDefinition Trigger { get; }

    /// <summary>The decision outcome the step needs, or <see langword="null"/>.</summary>
    public OutcomeDefinition? Outcome { get; }

    /// <summary>The leaf before the step.</summary>
    public Type From { get; }

    /// <summary>The leaf after it.</summary>
    public Type To { get; }

    /// <summary>Whether the step relies on its guard passing, or an earlier candidate's guard refusing, or a decision's outcome.</summary>
    public bool AssumesGuards { get; }

    /// <summary><c>From --[trigger]--> To: Transition</c>, with the outcome and an asterisk when guards are assumed.</summary>
    public override string ToString() =>
        $"{From.Name} --[{Trigger}]--> {To.Name}: {Transition.Name}{(Outcome is null ? string.Empty : " / " + Outcome.Name)}{(AssumesGuards ? " *" : string.Empty)}";
}
