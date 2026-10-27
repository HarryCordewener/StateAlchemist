using System.Linq;
using System.Threading.Tasks;
using StateAlchemist.Contracts.Machines;
using StateAlchemist.Contracts.Machines.Joins;
using TUnit.Core;

namespace StateAlchemist.Contracts.Suite;

/// <summary>docs/concepts/triggers.md: joins.</summary>
public abstract class JoinContract : MachineContract
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AJoinFiresOnceEveryEventHasArrivedInEitherOrder(bool paidFirst)
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Joins, context, new ContractHooks(context.Log));
        var paid = new Paid { Amount = 30 };
        var reserved = new Reserved { Warehouse = "north" };

        await (paidFirst ? machine.FireAsync(paid) : machine.FireAsync(reserved));
        await Assert.That(machine.IsIn<Browsing>()).IsTrue();
        await Assert.That(context.Trace).IsEqualTo("");

        await (paidFirst ? machine.FireAsync(reserved) : machine.FireAsync(paid));
        await Assert.That(machine.TryGetState<Shipped>(out var shipped)).IsTrue();
        await Assert.That(shipped.Label).IsEqualTo("north:30");
        await Assert.That(context.Trace).IsEqualTo("shipped north:30 paid 30 | transitioned JoinModule.Ship");
    }

    [Test]
    public async Task AnEventThatArrivesAgainReplacesItsPayload()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Joins, context);
        await machine.FireAsync(new Paid { Amount = 1 });
        await machine.FireAsync(new Paid { Amount = 2 });
        await machine.FireAsync(new Reserved { Warehouse = "south" });
        await Assert.That(machine.TryGetState<Shipped>(out var shipped)).IsTrue();
        await Assert.That(shipped.Label).IsEqualTo("south:2");
    }

    [Test]
    public async Task ArrivalsSurviveMovesBelowTheJoinsState()
    {
        var machine = await StartAsync(Shapes.Joins, new RecordingContext());
        await machine.FireAsync(new Paid { Amount = 5 });
        await machine.FireAsync((byte)1);
        await Assert.That(machine.IsIn<Reviewing>()).IsTrue();
        await machine.FireAsync(new Reserved { Warehouse = "east" });
        await Assert.That(machine.IsIn<Shipped>()).IsTrue();
    }

    [Test]
    public async Task LeavingTheJoinsStateForgetsItsArrivals()
    {
        var machine = await StartAsync(Shapes.Joins, new RecordingContext());
        await machine.FireAsync(new Paid { Amount = 5 });
        await machine.FireAsync((byte)9);
        await machine.FireAsync(new Reserved { Warehouse = "east" });
        await Assert.That(machine.IsIn<Browsing>()).IsTrue();

        await machine.FireAsync(new Paid { Amount = 6 });
        await Assert.That(machine.TryGetState<Shipped>(out var shipped)).IsTrue();
        await Assert.That(shipped.Label).IsEqualTo("east:6");
    }

    [Test]
    public async Task AJoinThatFiredWaitsForEveryEventAgain()
    {
        var machine = await StartAsync(Shapes.Joins, new RecordingContext());
        await machine.FireAsync(new Supplied());
        await machine.FireAsync(new Counted { Count = 3 });
        await machine.FireAsync(new Counted { Count = 4 });
        await Assert.That(machine.TryGetState<JoinRoot>(out var root)).IsTrue();
        await Assert.That(root.Restocked).IsEqualTo(3);

        await machine.FireAsync(new Supplied());
        await Assert.That(machine.TryGetState(out root)).IsTrue();
        await Assert.That(root.Restocked).IsEqualTo(7);
    }

    [Test]
    public async Task APlanSaysWhetherAnEventCompletesAJoin()
    {
        var context = new RecordingContext();
        var machine = await StartAsync(Shapes.Joins, context);

        var waiting = machine.Plan(new Paid());
        await Assert.That(waiting.Handled).IsTrue();
        await Assert.That(waiting.IsJoinArrival).IsTrue();
        await Assert.That(waiting.Transition).IsEqualTo("JoinModule.Ship");
        await Assert.That(waiting.Kind).IsEqualTo(TransitionKind.Stay);
        await Assert.That(waiting.Target).IsEqualTo(typeof(Browsing));

        await machine.FireAsync(new Reserved { Warehouse = "west" });
        var completing = machine.Plan(new Paid());
        await Assert.That(completing.IsJoinArrival).IsFalse();
        await Assert.That(completing.Target).IsEqualTo(typeof(Shipped));
        await Assert.That(string.Join(",", completing.Exiting.Select(t => t.Name))).IsEqualTo("Browsing,Checkout");
        await Assert.That(machine.IsIn<Browsing>()).IsTrue();
    }

    [Test]
    public async Task TheDefinitionListsAJoinsEvents()
    {
        var definition = Create(Shapes.Joins, new RecordingContext(), null).Definition;
        var ship = definition.Transitions.Where(t => t.Name == "JoinModule.Ship").ToList();
        await Assert.That(string.Join(",", ship.Select(t => t.Trigger.ToString()))).IsEqualTo("event Paid,event Reserved");
        await Assert.That(ship.All(t => t.IsJoin && string.Join(",", t.Joins.Select(e => e.Name)) == "Paid,Reserved")).IsTrue();
        await Assert.That(definition.Transitions.Single(t => t.Name == "JoinModule.Review").IsJoin).IsFalse();
    }
}
