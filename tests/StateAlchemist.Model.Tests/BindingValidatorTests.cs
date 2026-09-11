using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class BindingValidatorTests
{
    private readonly TestModel _model = new();
    private readonly int _idle;

    public BindingValidatorTests()
    {
        var root = _model.Root();
        _idle = _model.State("Idle", root, initial: true);
    }

    private string Problems() => BindingValidator.Validate(_model.Build()).Describe();

    [Test]
    public async Task AnUnknownTypeCannotBeBound()
    {
        _model.Add("Go", _idle, -1, TriggerModel.Value(1), new ParameterModel("now", "System.DateTime", ParameterKind.Unknown, Passing.Value));
        await Assert.That(Problems()).IsEqualTo("SALCH0204: Parameter 'now' of 'Go.Transform' cannot be bound: 'System.DateTime' is not a state, the value type, an event, the configuration, the context, or a CancellationToken");
    }

    [Test]
    public async Task AnEventTransitionHasNoValue()
    {
        _model.Add("Recover", _idle, -1, TriggerModel.Event("T.Error"), TestModel.Value());
        await Assert.That(Problems()).IsEqualTo("SALCH0204: Parameter 'value' of 'Recover.Transform' cannot be bound: this transition fires on an event, not a value");
    }

    [Test]
    public async Task AnEventParameterMustBeTheTriggeringEvent()
    {
        _model.Add("Recover", _idle, -1, TriggerModel.Event("T.Error"), new ParameterModel("e", "T.Timeout", ParameterKind.Event, Passing.In));
        await Assert.That(Problems()).IsEqualTo("SALCH0204: Parameter 'e' of 'Recover.Transform' cannot be bound: this transition fires on event Error, not 'T.Timeout'");
    }

    [Test]
    public async Task OnlyARunTransformTakesARun()
    {
        _model.Add("Capture", _idle, -1, TriggerModel.Any, new ParameterModel("run", "System.ReadOnlySpan<System.Byte>", ParameterKind.Run, Passing.Value));
        await Assert.That(Problems()).IsEqualTo("SALCH0204: Parameter 'run' of 'Capture.Transform' cannot be bound: only a [Run] transition's Transform takes a run");
    }

    [Test]
    public async Task ATransformCannotTakeACancellationToken()
    {
        _model.Add("Go", _idle, -1, TriggerModel.Value(1), new ParameterModel("ct", "System.Threading.CancellationToken", ParameterKind.CancellationToken, Passing.Value));
        await Assert.That(Problems()).IsEqualTo("SALCH0204: Parameter 'ct' of 'Go.Transform' cannot be bound: a Transform cannot take a CancellationToken");
    }
}
