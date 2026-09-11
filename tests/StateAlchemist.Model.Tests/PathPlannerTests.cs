using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

/// <summary>
/// Root ─┬─ Idle [Initial]
///       └─ Sub ─┬─ Awaiting [Initial]
///               └─ Naws
/// </summary>
public class PathPlannerTests
{
    private readonly TestModel _model = new();
    private readonly int _root;
    private readonly int _idle;
    private readonly int _sub;
    private readonly int _awaiting;
    private readonly int _naws;

    public PathPlannerTests()
    {
        _root = _model.Root();
        _idle = _model.State("Idle", _root, initial: true);
        _sub = _model.State("Sub", _root);
        _awaiting = _model.State("Awaiting", _sub, initial: true);
        _naws = _model.State("Naws", _sub);
    }

    private Hierarchy Tree => new(_model.Build().States);

    private static string Show(TransitionPath path) =>
        $"lca {path.Lca}; exit [{string.Join(",", path.Exiting)}]; enter [{string.Join(",", path.Entering)}]; leaf {path.TargetLeaf}";

    [Test]
    public async Task AStayExitsAndEntersNothing()
    {
        var stay = _model.Add("Capture", _naws, -1, TriggerModel.Any);
        await Assert.That(Show(PathPlanner.Plan(Tree, stay, _naws))).IsEqualTo($"lca {_naws}; exit []; enter []; leaf {_naws}");
    }

    [Test]
    public async Task AMoveBetweenSiblingsKeepsTheParent()
    {
        await Assert.That(Show(PathPlanner.Move(Tree, _awaiting, _naws)))
            .IsEqualTo($"lca {_sub}; exit [{_awaiting}]; enter [{_naws}]; leaf {_naws}");
    }

    [Test]
    public async Task AMoveIntoAParentEntersItsInitialPath()
    {
        await Assert.That(Show(PathPlanner.Move(Tree, _idle, _sub)))
            .IsEqualTo($"lca {_root}; exit [{_idle}]; enter [{_sub},{_awaiting}]; leaf {_awaiting}");
    }

    [Test]
    public async Task AMoveToAnAncestorStartsItOver()
    {
        await Assert.That(Show(PathPlanner.Move(Tree, _naws, _sub)))
            .IsEqualTo($"lca {_root}; exit [{_naws},{_sub}]; enter [{_sub},{_awaiting}]; leaf {_awaiting}");
    }

    [Test]
    public async Task AReentryExitsAndEntersTheSource()
    {
        var reenter = _model.Add("Restart", _naws, _naws, TriggerModel.Value(1));
        await Assert.That(Show(PathPlanner.Plan(Tree, reenter, _naws)))
            .IsEqualTo($"lca {_sub}; exit [{_naws}]; enter [{_naws}]; leaf {_naws}");
    }

    [Test]
    public async Task AMoveToTheRootNeverExitsTheRoot()
    {
        await Assert.That(Show(PathPlanner.Move(Tree, _naws, _root)))
            .IsEqualTo($"lca {_root}; exit [{_naws},{_sub}]; enter [{_idle}]; leaf {_idle}");
    }
}
