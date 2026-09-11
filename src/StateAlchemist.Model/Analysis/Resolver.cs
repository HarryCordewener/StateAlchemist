using System;
using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>
/// Which transitions a trigger may fire, in the order they are tried (spec §6.1). From the active leaf up to the root,
/// at each level: exact values, then ranges, then <c>[OnAny]</c>; within each, guarded transitions in
/// <see cref="TransitionModel.Order"/>, then the unguarded one. An unguarded candidate always fires, so the list ends
/// there. Events match only transitions on their type.
/// </summary>
public sealed class Resolver
{
    private readonly Hierarchy _hierarchy;
    private readonly List<TransitionModel>[] _bySource;

    /// <summary>Indexes <paramref name="model"/>'s transitions by source.</summary>
    public Resolver(MachineModel model, Hierarchy hierarchy)
    {
        _hierarchy = hierarchy;
        _bySource = new List<TransitionModel>[model.States.Count];
        for (var i = 0; i < _bySource.Length; i++)
        {
            _bySource[i] = [];
        }

        foreach (var transition in model.Transitions)
        {
            _bySource[transition.Source].Add(transition);
        }
    }

    /// <summary>The candidates for <paramref name="value"/> from <paramref name="leaf"/>, in the order they are tried.</summary>
    public IReadOnlyList<TransitionModel> ForValue(int leaf, long value)
    {
        var candidates = new List<TransitionModel>();
        for (var state = leaf; state >= 0; state = _hierarchy.ParentOf(state))
        {
            var level = _bySource[state];
            if (Add(candidates, level, t => t.Trigger.Kind == MatchKind.Value && t.Trigger.Low == value)
                || Add(candidates, level, t => t.Trigger.Kind == MatchKind.Range && t.Trigger.Matches(value))
                || Add(candidates, level, t => t.Trigger.Kind == MatchKind.Any))
            {
                break;
            }
        }

        return candidates;
    }

    /// <summary>The candidates for an event of <paramref name="eventType"/> from <paramref name="leaf"/>, in the order they are tried.</summary>
    public IReadOnlyList<TransitionModel> ForEvent(int leaf, string eventType)
    {
        var candidates = new List<TransitionModel>();
        for (var state = leaf; state >= 0; state = _hierarchy.ParentOf(state))
        {
            if (Add(candidates, _bySource[state], t => t.Trigger.Kind == MatchKind.Event && t.Trigger.EventType == eventType))
            {
                break;
            }
        }

        return candidates;
    }

    /// <summary>Adds the matching guarded transitions in order, then the unguarded one; true when an unguarded one was added.</summary>
    private static bool Add(List<TransitionModel> candidates, List<TransitionModel> level, Func<TransitionModel, bool> matches)
    {
        var matching = level.Where(matches).ToList();
        candidates.AddRange(matching.Where(t => t.IsGuarded).OrderBy(t => t.Order).ThenBy(t => t.Index));
        var unguarded = matching.FirstOrDefault(t => !t.IsGuarded);
        if (unguarded is null)
        {
            return false;
        }

        candidates.Add(unguarded);
        return true;
    }
}
