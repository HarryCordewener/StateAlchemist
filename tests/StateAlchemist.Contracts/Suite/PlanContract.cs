using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Recording;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/guides/testing.md: the pure layer.</summary>
public abstract class PlanContract : MachineContract
{
    [Test]
    public async Task APlanSaysWhatWouldHappenWithoutDoingIt()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context);
        context.Log.Clear();

        var plan = machine.Plan(1);

        await Assert.That(plan.Handled).IsTrue();
        await Assert.That(plan.Transition).IsEqualTo("RecorderModule.Sibling");
        await Assert.That(plan.Leaf).IsEqualTo(typeof(A1));
        await Assert.That(plan.Target).IsEqualTo(typeof(A2));
        await Assert.That(plan.Kind).IsEqualTo(TransitionKind.Move);
        await Assert.That(string.Join(",", plan.Exiting.Select(t => t.Name))).IsEqualTo("A1");
        await Assert.That(string.Join(",", plan.Entering.Select(t => t.Name))).IsEqualTo("A2");
        await Assert.That(machine.IsIn<A1>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("");
    }

    [Test]
    public async Task APlanForATriggerNothingHandlesIsNone()
    {
        var machine = await StartAsync(Shapes.Recorder, new RecordingContext());
        await Assert.That(machine.Plan(99).Handled).IsFalse();
        await Assert.That(machine.Plan(new Ping()).Transition).IsEqualTo("RecorderModule.Pinged");
    }

    [Test]
    public async Task APlanEvaluatesGuards()
    {
        var context = new RecordingContext();
        context.Allow.Add("Second");
        var machine = await StartAsync(Shapes.Guards, context);
        await Assert.That(machine.Plan(1).Transition).IsEqualTo("GuardModule.Second");
    }
}
