using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class JoinValidatorTests
{
    private readonly TestModel _model = new();
    private readonly int _idle;

    public JoinValidatorTests()
    {
        var root = _model.Root();
        _idle = _model.State("Idle", root, initial: true);
    }

    private void AddJoin(bool guarded)
    {
        var join = new JoinModel(["T.Paid", "T.Reserved"]);
        foreach (var e in join.Events)
        {
            _model.Add(new TransitionModel(0, "Ship", _idle, -1, TriggerModel.Event(e), 0, false,
                guarded ? TestModel.Method("Ship", "Guard", ReturnShape.Bool) : null, null, [], null, [], "T.Module", SourceSpan.None, join));
        }
    }

    [Test]
    public async Task AJoinWithoutAGuardIsAccepted()
    {
        AddJoin(guarded: false);
        await Assert.That(JoinValidator.Validate(_model.Build()).Describe()).IsEqualTo("");
    }

    [Test]
    public async Task AJoinWithAGuardIsReportedOnce()
    {
        AddJoin(guarded: true);
        await Assert.That(JoinValidator.Validate(_model.Build()).Describe()).IsEqualTo("SALCH0106: 'Ship' is a join, which cannot have a Guard");
    }

    [Test]
    [Arguments(1, false, "has an [OnAll] with fewer than two events: use [OnEvent]")]
    [Arguments(33, false, "has an [OnAll] with more than 32 events")]
    [Arguments(2, true, "lists a type in [OnAll] that is not an event type")]
    public async Task AnOnAllListIsChecked(int count, bool anyMissing, string problem)
    {
        await Assert.That(JoinModel.Problem(count, anyMissing, [])).IsEqualTo(problem);
    }

    [Test]
    public async Task AnEventListedTwiceIsReported()
    {
        await Assert.That(JoinModel.Problem(2, false, ["T.Paid", "T.Paid"])).IsEqualTo("lists 'T.Paid' twice in [OnAll]");
        await Assert.That(JoinModel.Problem(2, false, ["T.Paid", "T.Reserved"])).IsNull();
    }

    [Test]
    public async Task TheFullMaskHasOneBitPerEvent()
    {
        await Assert.That(new JoinModel(["A", "B", "C"]).Full).IsEqualTo(0b111u);
        await Assert.That(new JoinModel([.. new string[32]]).Full).IsEqualTo(uint.MaxValue);
    }
}
