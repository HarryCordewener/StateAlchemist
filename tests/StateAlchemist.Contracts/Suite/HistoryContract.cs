using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Recalling;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>Moves with history (docs/concepts/states.md, "Going back with history").</summary>
public abstract class HistoryContract : MachineContract
{
    private async Task<(IMachine<byte> Machine, RecordingContext Context)> Started(params byte[] first)
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Recalling, context);
        if (first.Length > 0)
        {
            await machine.FireAsync(first);
        }

        context.Log.Clear();
        return (machine, context);
    }

    private static T Data<T>(IMachine<byte> machine)
        where T : struct => machine.TryGetState(out T value) ? value : throw new System.InvalidOperationException($"{typeof(T).Name} is not active");

    [Test]
    public async Task APlainMoveEntersTheInitialPathWhateverWasActive()
    {
        var (machine, context) = await Started(3, 1, 2, 9);
        await machine.FireAsync((byte)3);
        await Assert.That(machine.IsIn<Stopped>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("entered Player | entered Stopped");
    }

    [Test]
    public async Task DeepHistoryEntersTheLeafThatWasActive()
    {
        var (machine, context) = await Started(3, 1, 2, 9);
        await machine.FireAsync((byte)5);
        await Assert.That(machine.IsIn<Fast>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("entered Player | entered Playing | entered Fast");
        await Assert.That(Data<Player>(machine).Resumes).IsEqualTo(1);
    }

    [Test]
    public async Task ShallowHistoryEntersTheChildThatWasActiveFromItsInitialPath()
    {
        var (machine, context) = await Started(3, 1, 2, 9);
        await machine.FireAsync((byte)4);
        await Assert.That(machine.IsIn<Slow>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("entered Player | entered Playing | entered Slow");
    }

    [Test]
    public async Task BeforeTheTargetWasEverExitedHistoryEntersTheInitialPath()
    {
        var (machine, context) = await Started();
        await machine.FireAsync((byte)5);
        await Assert.That(machine.IsIn<Stopped>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("entered Player | entered Stopped");
    }

    [Test]
    public async Task HistoryRestoresWhichStateIsActiveNotItsData()
    {
        var (machine, _) = await Started(3, 1, 2, 8, 8);
        await Assert.That(Data<Fast>(machine).Count).IsEqualTo(2);
        await machine.FireAsync(new byte[] { 9, 5 });
        await Assert.That(machine.IsIn<Fast>()).IsTrue();
        await Assert.That(Data<Fast>(machine).Count).IsEqualTo(0);
    }

    [Test]
    public async Task EachExitRecordsAgain()
    {
        var (machine, _) = await Started(3, 1, 2, 9, 3, 9);
        await machine.FireAsync((byte)5);
        await Assert.That(machine.IsIn<Stopped>()).IsTrue();
    }

    [Test]
    public async Task AMoveByHistoryToAStateOnTheActivePathStartsItOverInTheSameLeaf()
    {
        var (machine, context) = await Started(3, 1, 2, 8);
        await machine.FireAsync((byte)6);
        await Assert.That(machine.IsIn<Fast>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("exited Fast | exited Player | entered Player | entered Playing | entered Fast");
        await Assert.That(Data<Fast>(machine).Count).IsEqualTo(0);
        await Assert.That(Data<Player>(machine).Resumes).IsEqualTo(100);
    }

    [Test]
    public async Task ADecisionOutcomeCanMoveByHistory()
    {
        var (machine, _) = await Started(3, 1, 2, 9);
        await machine.FireAsync((byte)7);
        await Assert.That(machine.IsIn<Fast>()).IsTrue();
        await Assert.That(Data<Player>(machine).Resumes).IsEqualTo(1);
    }

    [Test]
    public async Task APlanNamesTheLeafHistoryWouldEnter()
    {
        var (machine, _) = await Started(3, 1, 2, 9);
        var plan = machine.Plan(5);
        await Assert.That(plan.Target).IsEqualTo(typeof(Fast));
        await Assert.That(plan.Entering).IsEquivalentTo(new[] { typeof(Player), typeof(Playing), typeof(Fast) });
        await Assert.That(machine.Plan(4).Target).IsEqualTo(typeof(Slow));
        await Assert.That(machine.Plan(3).Target).IsEqualTo(typeof(Stopped));
    }

    [Test]
    public async Task TheDefinitionSaysWhichMovesUseHistory()
    {
        var (machine, _) = await Started();
        var transitions = machine.Definition.Transitions;
        await Assert.That(transitions.Single(t => t.Name == "RecallingModule.ResumeDeep").History).IsEqualTo(History.Deep);
        await Assert.That(transitions.Single(t => t.Name == "RecallingModule.ResumeShallow").History).IsEqualTo(History.Shallow);
        await Assert.That(transitions.Single(t => t.Name == "RecallingModule.Restart").History).IsEqualTo(History.None);
        var outcomes = transitions.Single(t => t.Name == "RecallingModule.Ask").Outcomes;
        await Assert.That(outcomes.Single(o => o.Name == "Resume").History).IsEqualTo(History.Deep);
        await Assert.That(outcomes.Single(o => o.Name == "Fresh").History).IsEqualTo(History.None);
    }
}
