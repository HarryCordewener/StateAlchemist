using System;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Tests.Runtime;

public class MachineDefinitionTests
{
    private struct Root : IRootState { }
    private struct A : IState<Root> { }
    private struct B : IState<Root> { }

    private static MachineDefinition TwoChildren() => new(
        typeof(byte),
        [new StateDefinition(0, typeof(Root), -1, false), new StateDefinition(1, typeof(A), 0, true), new StateDefinition(2, typeof(B), 0, false)],
        [new TransitionDefinition(0, "M.Go", 1, 2, TransitionKind.Move, TriggerDefinition.ForValue(1), 0, false, false, false, false)]);

    [Test]
    public async Task ItFindsStatesAndChildren()
    {
        var definition = TwoChildren();
        await Assert.That(definition.Root.Type).IsEqualTo(typeof(Root));
        await Assert.That(definition.IndexOf(typeof(B))).IsEqualTo(2);
        await Assert.That(definition.IndexOf(typeof(string))).IsEqualTo(-1);
        await Assert.That(string.Join(",", definition.ChildrenOf(definition.Root).Select(s => s.Name))).IsEqualTo("A,B");
    }

    [Test]
    public async Task ItRejectsAMisnumberedState()
    {
        await Assert.That(() => new MachineDefinition(typeof(byte), [new StateDefinition(1, typeof(Root), -1, false)], []))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ItRejectsTwoRoots()
    {
        await Assert.That(() => new MachineDefinition(
                typeof(byte),
                [new StateDefinition(0, typeof(Root), -1, false), new StateDefinition(1, typeof(A), -1, false)],
                []))
            .Throws<ArgumentException>();
    }

    /// <summary>A decision's outcomes name states too, and a diagram drawn from them would point at nothing.</summary>
    [Test]
    public async Task ItRejectsAnOutcomeToAMissingState()
    {
        await Assert.That(() => new MachineDefinition(
                typeof(byte),
                [new StateDefinition(0, typeof(Root), -1, false), new StateDefinition(1, typeof(A), 0, true)],
                [
                    new TransitionDefinition(0, "M.Ask", 1, -1, TransitionKind.Stay, TriggerDefinition.ForAny(), 0, false, false, false, true,
                        [new OutcomeDefinition(typeof(B), 7)]),
                ]))
            .Throws<ArgumentException>();
    }

    /// <summary>A transition made without outcomes has none, rather than a null nobody can read.</summary>
    [Test]
    public async Task ATransitionWithoutOutcomesHasAnEmptyList()
    {
        await Assert.That(TwoChildren().Transitions[0].Outcomes).IsEmpty();
    }

    [Test]
    public async Task ItRejectsATransitionToAMissingState()
    {
        await Assert.That(() => new MachineDefinition(
                typeof(byte),
                [new StateDefinition(0, typeof(Root), -1, false)],
                [new TransitionDefinition(0, "M.Go", 0, 7, TransitionKind.Move, TriggerDefinition.ForAny(), 0, false, false, false, false)]))
            .Throws<ArgumentException>();
    }
}
