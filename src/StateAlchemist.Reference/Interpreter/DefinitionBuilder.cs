using System;
using System.Linq;
using StateAlchemist.Model;

namespace StateAlchemist.Reference;

/// <summary>Describes a reflected machine as a <see cref="MachineDefinition"/> — the same data a generated machine exposes.</summary>
internal static class DefinitionBuilder
{
    public static MachineDefinition Build(ReflectedMachine machine)
    {
        var model = machine.Model;
        var states = model.States
            .Select(s => new StateDefinition(s.Index, machine.StateTypes[s.Index], s.Parent, s.IsInitial))
            .ToList();
        var transitions = model.Transitions
            .Select(t => new TransitionDefinition(
                t.Index,
                t.Name,
                t.Source,
                t.Target,
                (TransitionKind)t.Kind,
                Trigger(t.Trigger),
                t.Order,
                t.IsGuarded,
                t.IsRun,
                new[] { t.Guard, t.Transform }.OfType<MethodModel>().Any(m => m.Parameters.Any(p => p.Kind == ParameterKind.Context)),
                t.IsDecision))
            .ToList();
        return new MachineDefinition(machine.Spec.Value!, states, transitions);
    }

    private static TriggerDefinition Trigger(TriggerModel trigger) => trigger.Kind switch
    {
        MatchKind.Value => TriggerDefinition.ForValue(trigger.Low),
        MatchKind.Range => TriggerDefinition.ForRange(trigger.Low, trigger.High),
        MatchKind.Any => TriggerDefinition.ForAny(),
        _ => TriggerDefinition.ForEvent(FindType(trigger.EventType!)),
    };

    private static Type FindType(string fullName) =>
        AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t is not null)
        ?? throw new InvalidOperationException($"Event type '{fullName}' is not loaded.");
}
