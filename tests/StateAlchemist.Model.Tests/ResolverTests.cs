using System.Linq;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

/// <summary>Root ─── Willing [Initial]</summary>
public class ResolverTests
{
    private readonly TestModel _model = new();
    private readonly int _root;
    private readonly int _willing;

    public ResolverTests()
    {
        _root = _model.Root();
        _willing = _model.State("Willing", _root, initial: true);
    }

    private string Candidates(long value)
    {
        var built = _model.Build();
        var resolver = new Resolver(built, new Hierarchy(built.States));
        return string.Join(",", resolver.ForValue(_willing, value).Select(t => t.Name));
    }

    [Test]
    public async Task ExactBeatsRangeBeatsAnyWithinALevel()
    {
        _model.Add("Any", _willing, -1, TriggerModel.Any);
        _model.Add("Range", _willing, -1, TriggerModel.Range(200, 210));
        _model.Add("Exact", _willing, -1, TriggerModel.Value(201));
        await Assert.That(Candidates(201)).IsEqualTo("Exact");
        await Assert.That(Candidates(205)).IsEqualTo("Range");
        await Assert.That(Candidates(42)).IsEqualTo("Any");
    }

    [Test]
    public async Task AChildsOrElseShadowsItsParent()
    {
        _model.Add("RootExact", _root, -1, TriggerModel.Value(7));
        _model.Add("ChildAny", _willing, -1, TriggerModel.Any);
        await Assert.That(Candidates(7)).IsEqualTo("ChildAny");
    }

    [Test]
    public async Task GuardedCandidatesComeInOrderThenTheUnguardedOne()
    {
        _model.Add("Fallback", _willing, -1, TriggerModel.Value(1));
        _model.Add("Second", _willing, -1, TriggerModel.Value(1), guarded: true, order: 2);
        _model.Add("First", _willing, -1, TriggerModel.Value(1), guarded: true, order: 1);
        await Assert.That(Candidates(1)).IsEqualTo("First,Second,Fallback");
    }

    [Test]
    public async Task WhenEveryCandidateIsGuardedResolutionCarriesOnUpward()
    {
        _model.Add("Guarded", _willing, -1, TriggerModel.Value(1), guarded: true, order: 0);
        _model.Add("ChildAny", _willing, -1, TriggerModel.Any, guarded: true, order: 0);
        _model.Add("Parent", _root, -1, TriggerModel.Value(1));
        await Assert.That(Candidates(1)).IsEqualTo("Guarded,ChildAny,Parent");
    }

    [Test]
    public async Task OrElseNeverMatchesAnEvent()
    {
        _model.Add("ChildAny", _willing, -1, TriggerModel.Any);
        _model.Add("Recover", _root, _willing, TriggerModel.Event("T.Error"));
        var built = _model.Build();
        var resolver = new Resolver(built, new Hierarchy(built.States));
        await Assert.That(string.Join(",", resolver.ForEvent(_willing, "T.Error").Select(t => t.Name))).IsEqualTo("Recover");
    }
}
