using System.Linq;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

/// <summary>
/// Root ─┬─ Idle [Initial]
///       └─ Player ─┬─ Stopped [Initial]
///                  └─ Playing ─┬─ Slow [Initial]
///                              └─ Fast
/// </summary>
public class HistoryTests
{
    private readonly TestModel _model = new();
    private readonly int _root;
    private readonly int _idle;
    private readonly int _player;
    private readonly int _stopped;
    private readonly int _playing;
    private readonly int _slow;
    private readonly int _fast;

    public HistoryTests()
    {
        _root = _model.Root();
        _idle = _model.State("Idle", _root, initial: true);
        _player = _model.State("Player", _root);
        _stopped = _model.State("Stopped", _player, initial: true);
        _playing = _model.State("Playing", _player);
        _slow = _model.State("Slow", _playing, initial: true);
        _fast = _model.State("Fast", _playing);
    }

    private Hierarchy Tree => new(_model.Build().States);

    private TransitionModel Resume(HistoryKind history, params ParameterModel[] transform) =>
        _model.Replace(_model.Add("Resume", _idle, _player, TriggerModel.Value(1), transform) with { History = history });

    private string Problems()
    {
        var built = _model.Build();
        var tree = new Hierarchy(built.States);
        return HistoryValidator.Validate(built, tree).Concat(RoleValidator.Validate(built, tree)).Describe();
    }

    [Test]
    public async Task DeepHistoryRecallsTheRecordedLeaf()
    {
        await Assert.That(PathPlanner.Recall(Tree, _player, HistoryKind.Deep, _fast)).IsEqualTo(_fast);
    }

    [Test]
    public async Task ShallowHistoryRecallsTheRecordedChildsInitialLeaf()
    {
        await Assert.That(PathPlanner.Recall(Tree, _player, HistoryKind.Shallow, _fast)).IsEqualTo(_slow);
        await Assert.That(PathPlanner.Recall(Tree, _playing, HistoryKind.Shallow, _fast)).IsEqualTo(_fast);
    }

    [Test]
    public async Task NothingRecordedRecallsTheInitialLeaf()
    {
        await Assert.That(PathPlanner.Recall(Tree, _player, HistoryKind.Deep, -1)).IsEqualTo(_stopped);
        await Assert.That(PathPlanner.Recall(Tree, _player, HistoryKind.Deep, _player)).IsEqualTo(_stopped);
        await Assert.That(PathPlanner.Recall(Tree, _player, HistoryKind.Deep, _idle)).IsEqualTo(_stopped);
    }

    [Test]
    public async Task AMoveFromOutsideHasOnePathPerRecallableLeafInitialFirst()
    {
        var deep = PathPlanner.Moves(Tree, _idle, _player, HistoryKind.Deep).Select(p => p.TargetLeaf).ToList();
        var shallow = PathPlanner.Moves(Tree, _idle, _player, HistoryKind.Shallow).Select(p => p.TargetLeaf).ToList();
        await Assert.That(deep).IsEquivalentTo(new[] { _stopped, _fast, _slow });
        await Assert.That(deep[0]).IsEqualTo(_stopped);
        await Assert.That(shallow).IsEquivalentTo(new[] { _stopped, _slow });
    }

    [Test]
    public async Task AMoveToATargetOnTheActivePathRecallsTheActiveLeaf()
    {
        var paths = PathPlanner.Moves(Tree, _fast, _player, HistoryKind.Deep);
        await Assert.That(paths.Count).IsEqualTo(1);
        await Assert.That(paths[0].TargetLeaf).IsEqualTo(_fast);
        await Assert.That(paths[0].Exiting).IsEquivalentTo(new[] { _fast, _playing, _player });
        await Assert.That(PathPlanner.Moves(Tree, _fast, _player, HistoryKind.Shallow)[0].TargetLeaf).IsEqualTo(_slow);
    }

    [Test]
    public async Task WithoutHistoryAMoveHasOnePath()
    {
        var paths = PathPlanner.Moves(Tree, _idle, _player, HistoryKind.None);
        await Assert.That(paths.Count).IsEqualTo(1);
        await Assert.That(paths[0].TargetLeaf).IsEqualTo(_stopped);
    }

    [Test]
    public async Task AMoveByHistoryMayWriteItsTarget()
    {
        Resume(HistoryKind.Deep, _model.Ref(_player));
        await Assert.That(Problems()).IsEqualTo("");
    }

    [Test]
    public async Task AMoveByHistoryCannotNameAStateUnderItsTarget()
    {
        Resume(HistoryKind.Deep, _model.Ref(_stopped));
        await Assert.That(Problems()).IsEqualTo(
            "SALCH0202: Parameter 'stopped' of 'Resume.Transform' names 'Stopped', which is under 'Player', which this move enters by history: what it enters there is known only when it runs");
    }

    [Test]
    public async Task HistoryOnALeafTargetIsSalch0210()
    {
        _model.Replace(_model.Add("Go", _idle, _fast, TriggerModel.Value(1)) with { History = HistoryKind.Deep });
        await Assert.That(Problems()).IsEqualTo("SALCH0210: 'Go' asks for history, but its target 'Fast' has no children in this machine");
    }

    [Test]
    public async Task HistoryOnTheRootIsSalch0210()
    {
        _model.Replace(_model.Add("Reset", _fast, _root, TriggerModel.Value(1)) with { History = HistoryKind.Shallow });
        await Assert.That(Problems()).IsEqualTo("SALCH0210: 'Reset' asks for history, but its target 'Root' is the root, which is never exited");
    }

    [Test]
    public async Task HistoryOnAStayIsSalch0210()
    {
        _model.Replace(_model.Add("Tick", _fast, -1, TriggerModel.Value(1)) with { History = HistoryKind.Deep });
        await Assert.That(Problems()).IsEqualTo("SALCH0210: 'Tick' asks for history, but it is a stay, which enters nothing");
    }

    [Test]
    public async Task HistoryOnADecisionOutcomeIsCheckedToo()
    {
        var decision = _model.Decision("Ask", _idle, _player, _fast, [], [], []);
        var completions = decision.Decision!.Completions.Select(c => c with { History = HistoryKind.Deep }).ToList();
        _model.Replace(decision with { Decision = decision.Decision with { Completions = completions } });
        await Assert.That(Problems()).IsEqualTo("SALCH0210: 'Ask.Complete' asks for history, but its target 'Fast' has no children in this machine");
    }
}
