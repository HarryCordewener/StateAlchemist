using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Deciding;
using StateAlchemist.Contracts.Machines.Recording;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/guides/testing.md: the machine as data.</summary>
public abstract class DefinitionContract : MachineContract
{
    [Test]
    public async Task TheDefinitionListsStatesRootFirstWithTheirParents()
    {
        var definition = Create(Shapes.Recorder, new RecordingContext(), null).Definition;
        await Assert.That(definition.Root.Type).IsEqualTo(typeof(Root));
        await Assert.That(string.Join(",", definition.States.Select(s => s.Name))).IsEqualTo("Root,A,A1,A2,B,B1");
        await Assert.That(string.Join(",", definition.ChildrenOf(definition.States[definition.IndexOf(typeof(A))]).Select(s => s.Name))).IsEqualTo("A1,A2");
        await Assert.That(definition.States[definition.IndexOf(typeof(B1))].IsInitial).IsTrue();
    }

    [Test]
    public async Task TheDefinitionDescribesEachTransition()
    {
        var definition = Create(Shapes.Recorder, new RecordingContext(), null).Definition;
        var sibling = definition.Transitions.Single(t => t.Name == "RecorderModule.Sibling");
        await Assert.That(sibling.Kind).IsEqualTo(TransitionKind.Move);
        await Assert.That(sibling.Trigger.ToString()).IsEqualTo("1");
        await Assert.That(definition.States[sibling.Target].Name).IsEqualTo("A2");
        await Assert.That(definition.Transitions.Single(t => t.Name == "RecorderModule.Pinged").Trigger.ToString()).IsEqualTo("event Ping");
        await Assert.That(definition.Transitions.Single(t => t.Name == "RecorderModule.Stay").Kind).IsEqualTo(TransitionKind.Stay);
    }

    /// <summary>
    /// A decision's outcomes, and the state each one moves to. The decision itself has no target — which one it
    /// takes is not known when the trigger arrives — so without these the states only a decision reaches are
    /// named by nothing, and a diagram drawn from the definition shows them as unreachable.
    /// </summary>
    [Test]
    public async Task TheDefinitionNamesEachDecisionOutcomeAndWhereItGoes()
    {
        var definition = Create(Shapes.Deciding, new RecordingContext(), null).Definition;

        var ask = definition.Transitions.Single(t => t.Name == "DecidingModule.Ask");
        await Assert.That(ask.IsDecision).IsTrue();
        await Assert.That(ask.Target).IsEqualTo(-1);
        await Assert.That(string.Join(",", ask.Outcomes.Select(o => $"{o.Name}->{definition.States[o.Target].Name}")))
            .IsEqualTo("Accept->Account,Reject->Refused");
        await Assert.That(ask.Outcomes.Select(o => o.Type)).IsEquivalentTo(new[] { typeof(Accept), typeof(Reject) });

        await Assert.That(definition.Transitions.Single(t => t.Name == "DecidingModule.Again").Outcomes).IsEmpty();
    }

    [Test]
    public async Task TheDefinitionRecordsGuardsAndContextUse()
    {
        var definition = Create(Shapes.Guards, new RecordingContext(), null).Definition;
        var first = definition.Transitions.Single(t => t.Name == "GuardModule.First");
        await Assert.That(first.HasGuard).IsTrue();
        await Assert.That(first.UsesContext).IsTrue();
        await Assert.That(definition.Transitions.Single(t => t.Name == "GuardModule.Fallback").UsesContext).IsFalse();
    }
}
