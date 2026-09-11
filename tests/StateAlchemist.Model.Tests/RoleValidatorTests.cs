using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class RoleValidatorTests
{
    private readonly TestModel _model = new();
    private readonly int _root;
    private readonly int _idle;
    private readonly int _sub;
    private readonly int _awaiting;
    private readonly int _naws;

    public RoleValidatorTests()
    {
        _root = _model.Root(hasData: true);
        _idle = _model.State("Idle", _root, initial: true);
        _sub = _model.State("Sub", _root, hasData: true);
        _awaiting = _model.State("Awaiting", _sub, initial: true);
        _naws = _model.State("Naws", _sub, hasData: true);
    }

    private string Problems()
    {
        var built = _model.Build();
        return RoleValidator.Validate(built, new Hierarchy(built.States)).Describe();
    }

    [Test]
    public async Task ASiblingMoveMayWriteTheParentAndTheTarget()
    {
        _model.Add("Begin", _awaiting, _naws, TriggerModel.Value(31), _model.In(_awaiting), _model.Ref(_sub), _model.Ref(_naws));
        await Assert.That(Problems()).IsEqualTo("");
    }

    [Test]
    public async Task WritingAnExitingStateIsSalch0201()
    {
        _model.Add("Leave", _naws, _idle, TriggerModel.Value(1), _model.Ref(_sub));
        await Assert.That(Problems()).IsEqualTo("SALCH0201: Parameter 'sub' of 'Leave.Transform' takes 'Sub' by ref, but the transition exits it; take it as in");
    }

    [Test]
    public async Task NamingAStateTheTransitionDoesNotTouchIsSalch0202()
    {
        _model.Add("Begin", _awaiting, _naws, TriggerModel.Value(31), _model.In(_idle));
        await Assert.That(Problems()).IsEqualTo("SALCH0202: Parameter 'idle' of 'Begin.Transform' names 'Idle', which has no role in this transition");
    }

    [Test]
    public async Task AnAncestorDeclaredTransitionMayNameAStateThatBindsTheSameWayFromEveryLeaf()
    {
        // From Sub to Naws: from Awaiting, Naws is entering; from Naws itself, Naws is started over. Either way
        // `ref Naws` writes the fresh Naws, and `ref Sub` the Sub that stays.
        _model.Add("Jump", _sub, _naws, TriggerModel.Value(9), _model.Ref(_sub), _model.Ref(_naws));
        await Assert.That(Problems()).IsEqualTo("");
    }

    [Test]
    public async Task AStateTouchedFromOnlySomeLeavesIsSalch0202()
    {
        // `in Awaiting` reads the state being left when the leaf is Awaiting; from Naws there is nothing to read.
        _model.Add("Peek", _sub, _naws, TriggerModel.Value(8), _model.In(_awaiting));
        await Assert.That(Problems()).IsEqualTo("SALCH0202: Parameter 'awaiting' of 'Peek.Transform' names 'Awaiting', which is not touched from every leaf this transition fires from");
    }

    [Test]
    public async Task AReentryMayReadTheOldDataAndWriteTheNew()
    {
        _model.Add("Restart", _naws, _naws, TriggerModel.Value(1), _model.In(_naws), _model.Ref(_naws));
        await Assert.That(Problems()).IsEqualTo("SALCH0301: 'Restart' re-enters 'Naws', which has data; the data is cleared");
    }

    [Test]
    public async Task ReenteringTheRootClearsNothingSoIsNotWarned()
    {
        _model.Add("Restart", _root, _root, TriggerModel.Value(6), _model.Ref(_root));
        await Assert.That(Problems()).IsEqualTo("");
    }

    [Test]
    public async Task AGuardOnlyReads()
    {
        var guarded = _model.Add("Maybe", _naws, _idle, TriggerModel.Value(2), guarded: true, order: 0);
        _model.Add(guarded with
        {
            Index = 0,
            Trigger = TriggerModel.Value(3),
            Guard = TestModel.Method("Maybe", "Guard", ReturnShape.Bool, _model.Ref(_naws)),
        });
        await Assert.That(Problems()).IsEqualTo("SALCH0204: Parameter 'naws' of 'Maybe.Guard' cannot be bound: a guard only reads states: take it as in");
    }

    [Test]
    public async Task AStateActionReadsItsStateAndItsAncestors()
    {
        _model.Action(ActionPhase.Exited, _naws, "T.Module", 0, _model.In(_naws), _model.In(_sub), _model.In(_idle));
        await Assert.That(Problems()).IsEqualTo("SALCH0202: Parameter 'idle' of 'T.Module.OnExited' names 'Idle', which is not 'Naws' or one of its ancestors");
    }
}
