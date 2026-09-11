using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Recording;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/transitions.md, docs/concepts/states.md and the order of docs/concepts/actions.md.</summary>
public abstract class TransitionContract : MachineContract
{
    private async Task<(IMachine<byte> Machine, RecordingContext Context)> Started(ContractHooks? hooks = null, RecordingContext? context = null)
    {
        context ??= new RecordingContext();
        var machine = await StartAsync(Shapes.Recorder, context, hooks);
        context.Log.Clear();
        return (machine, context);
    }

    private static T Data<T>(IMachine<byte> machine)
        where T : struct => machine.TryGetState(out T value) ? value : throw new System.InvalidOperationException($"{typeof(T).Name} is not active");

    [Test]
    public async Task ASiblingMoveWritesTheParentAndRunsEveryStepInOrder()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)1);
        await Assert.That(context.Trace).IsEqualTo("exited A1 (0) | exited A1 (extra) | entered A2 | completed Sibling");
        await Assert.That(machine.IsIn<A2>()).IsTrue();
        await Assert.That(machine.IsIn<A>()).IsTrue();
        await Assert.That(machine.IsIn<A1>()).IsFalse();
        await Assert.That(Data<A>(machine).Value).IsEqualTo(1);
        await Assert.That(Data<A2>(machine).Value).IsEqualTo(10);
    }

    [Test]
    public async Task ACousinMoveExitsInnermostFirstAndEntersOutermostFirst()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)2);
        await Assert.That(context.Trace).IsEqualTo("exited A1 (0) | exited A1 (extra) | exited A (0) | entered B | entered B1 | completed Cousin");
        await Assert.That(Data<B>(machine).Value).IsEqualTo(7);
        await Assert.That(Data<B1>(machine).Count).IsEqualTo(1);
    }

    [Test]
    public async Task AStayRunsNoStateActionsAndKeepsItsData()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)3);
        await machine.FireAsync((byte)3);
        await Assert.That(context.Trace).IsEqualTo("");
        await Assert.That(Data<A1>(machine).Value).IsEqualTo(2);
    }

    [Test]
    public async Task AReentryReadsTheOldDataAndWritesTheNew()
    {
        var (machine, context) = await Started();
        await machine.FireAsync(new byte[] { 3, 3 });
        await machine.FireAsync((byte)4);
        await Assert.That(context.Trace).IsEqualTo("exited A1 (2) | exited A1 (extra) | entered A1 (200)");
        await Assert.That(Data<A1>(machine).Value).IsEqualTo(200);
    }

    [Test]
    public async Task AParentDeclaredTransitionMovesFromOneLeafAndStartsTheOtherOver()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)5);
        await Assert.That(context.Trace).IsEqualTo("exited A1 (0) | exited A1 (extra) | entered A1 (50)");

        await machine.FireAsync((byte)1);
        context.Log.Clear();
        await machine.FireAsync((byte)5);
        await Assert.That(context.Trace).IsEqualTo("exited A2 | entered A1 (50)");
        await Assert.That(Data<A>(machine).Value).IsEqualTo(1);
    }

    [Test]
    public async Task AMoveToTheRootNeverExitsTheRoot()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)6);
        await Assert.That(context.Trace).IsEqualTo("exited A1 (0) | exited A1 (extra) | exited A (0) | entered A | entered A1 (0)");
        await Assert.That(Data<Root>(machine).Counter).IsEqualTo(-1);
    }

    [Test]
    public async Task LeavingAStateClearsItsData()
    {
        var (machine, _) = await Started();
        await machine.FireAsync(new byte[] { 1, 5 });
        await Assert.That(Data<A>(machine).Value).IsEqualTo(1);

        await machine.FireAsync(new byte[] { 2, 7 });
        await Assert.That(Data<A>(machine).Value).IsEqualTo(0);
        await Assert.That(Data<Root>(machine).Counter).IsEqualTo(51);
    }

    [Test]
    public async Task AStateWithResetKeepsWhatResetKeeps()
    {
        var (machine, _) = await Started();
        await machine.FireAsync(new byte[] { 2, 8, 8 });
        var first = Data<B1>(machine);
        await Assert.That(first.Count).IsEqualTo(3);

        await machine.FireAsync(new byte[] { 7, 2 });
        var second = Data<B1>(machine);
        await Assert.That(second.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(first.Buffer, second.Buffer)).IsTrue();
    }

    [Test]
    public async Task TheTransitionedHookSeesEveryTransition()
    {
        var context = new RecordingContext();
        var (machine, _) = await Started(new ContractHooks(context.Log), context);
        await machine.FireAsync(new byte[] { 3, 1 });
        await Assert.That(context.Trace).IsEqualTo(
            "transitioned RecorderModule.Stay | exited A1 (1) | exited A1 (extra) | entered A2 | completed Sibling | transitioned RecorderModule.Sibling");
    }
}
