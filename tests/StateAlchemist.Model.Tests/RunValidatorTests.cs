using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class RunValidatorTests
{
    private readonly TestModel _model = new();
    private readonly int _root;
    private readonly int _payload;

    public RunValidatorTests()
    {
        _root = _model.Root();
        _payload = _model.State("Payload", _root, initial: true, hasData: true);
    }

    private static ParameterModel Run() => new("run", "System.ReadOnlySpan<System.Byte>", ParameterKind.Run, Passing.Value);

    private TransitionModel RunTransition(int target, TriggerModel trigger, params ParameterModel[] parameters) =>
        new(0, "Capture", _payload, target, trigger, 0, IsRun: true, null,
            TestModel.Method("Capture", "Transform", ReturnShape.Void, parameters), [], null, [], "T.Module", SourceSpan.None);

    [Test]
    public async Task AWellFormedRunIsAccepted()
    {
        _model.Add(RunTransition(-1, TriggerModel.Any, _model.Ref(_payload), Run()));
        await Assert.That(RunValidator.Validate(_model.Build()).Describe()).IsEqualTo("");
    }

    [Test]
    public async Task ARunMustBeAStay()
    {
        _model.Add(RunTransition(_root, TriggerModel.Any, _model.Ref(_payload), Run()));
        await Assert.That(RunValidator.Validate(_model.Build()).Describe()).IsEqualTo("SALCH0701: [Run] on 'Capture' is invalid: a run transition must be a stay");
    }

    [Test]
    public async Task ARunTakesItsStateAndTheRunFirst()
    {
        _model.Add(RunTransition(-1, TriggerModel.Any, Run(), _model.Ref(_payload)));
        await Assert.That(RunValidator.Validate(_model.Build()).Describe())
            .IsEqualTo("SALCH0701: [Run] on 'Capture' is invalid: its Transform must take (ref Payload self, ReadOnlySpan<System.Byte> run), optionally followed by the configuration and the context");
    }
}
