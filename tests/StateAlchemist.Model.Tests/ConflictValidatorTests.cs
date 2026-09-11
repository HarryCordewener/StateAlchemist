using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class ConflictValidatorTests
{
    private readonly TestModel _model = new();
    private readonly int _willing;

    public ConflictValidatorTests()
    {
        var root = _model.Root();
        _willing = _model.State("Willing", root, initial: true);
    }

    private string Problems() => ConflictValidator.Validate(_model.Build()).Describe();

    [Test]
    public async Task TwoUnguardedTransitionsOnTheSameValueConflict()
    {
        _model.Add("Gmcp.Accept", _willing, -1, TriggerModel.Value(201));
        _model.Add("Other.Accept", _willing, -1, TriggerModel.Value(201));
        await Assert.That(Problems()).IsEqualTo("SALCH0101: 'Gmcp.Accept' and 'Other.Accept' both handle 201 in state 'Willing' without a guard");
    }

    [Test]
    public async Task OverlappingRangesConflictOnTheirOverlap()
    {
        _model.Add("Low", _willing, -1, TriggerModel.Range(0, 20));
        _model.Add("High", _willing, -1, TriggerModel.Range(10, 30));
        await Assert.That(Problems()).IsEqualTo("SALCH0101: 'Low' and 'High' both handle 10..20 in state 'Willing' without a guard");
    }

    [Test]
    public async Task AnExactValueAndARangeNeverConflict()
    {
        _model.Add("Exact", _willing, -1, TriggerModel.Value(15));
        _model.Add("Range", _willing, -1, TriggerModel.Range(10, 30));
        _model.Add("Any", _willing, -1, TriggerModel.Any);
        await Assert.That(Problems()).IsEqualTo("");
    }

    [Test]
    public async Task GuardedTransitionsNeedDistinctOrders()
    {
        _model.Add("A", _willing, -1, TriggerModel.Value(1), guarded: true, order: 3);
        _model.Add("B", _willing, -1, TriggerModel.Value(1), guarded: true, order: 3);
        await Assert.That(Problems()).IsEqualTo("SALCH0102: Guarded transitions 'A' and 'B' both handle 1 in state 'Willing' with Order 3");
    }

    [Test]
    public async Task ActionsFromDifferentModulesNeedDistinctOrders()
    {
        _model.Action(ActionPhase.Exited, _willing, "T.One", 0);
        _model.Action(ActionPhase.Exited, _willing, "T.Two", 0);
        _model.Action(ActionPhase.Exited, _willing, "T.Two", 0);
        await Assert.That(Problems()).IsEqualTo(
            "SALCH0103: 'T.One.OnExited' and 'T.Two.OnExited' both run when 'Willing' is exited, from different modules, with Order 0\n" +
            "SALCH0103: 'T.One.OnExited' and 'T.Two.OnExited' both run when 'Willing' is exited, from different modules, with Order 0");
    }
}
