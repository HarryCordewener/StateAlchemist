using System.Collections.Generic;
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

    /// <summary>The model as built, checked against its own hierarchy.</summary>
    private IReadOnlyList<ModelDiagnostic> Validate()
    {
        var model = _model.Build();
        return RunValidator.Validate(model, new Hierarchy(model.States));
    }

    private TransitionModel RunTransition(int target, TriggerModel trigger, params ParameterModel[] parameters) =>
        new(0, "Capture", _payload, target, trigger, 0, IsRun: true, null,
            TestModel.Method("Capture", "Transform", ReturnShape.Void, parameters), [], null, [], "T.Module", SourceSpan.None);

    [Test]
    public async Task AWellFormedRunIsAccepted()
    {
        _model.Add(RunTransition(-1, TriggerModel.Any, _model.Ref(_payload), Run()));
        await Assert.That(Validate().Describe()).IsEqualTo("");
    }

    [Test]
    public async Task ARunMustBeAStay()
    {
        _model.Add(RunTransition(_root, TriggerModel.Any, _model.Ref(_payload), Run()));
        await Assert.That(Validate().Describe()).IsEqualTo("SALCH0701: [Run] on 'Capture' is invalid: a run transition must be a stay");
    }

    /// <summary>
    /// What the telnet core hit: a newline handled on the parent, a run of text in the child. The child's trigger
    /// wins, and because it is a run the newline vanishes into it rather than ending the line.
    /// </summary>
    [Test]
    public async Task ARunThatSwallowsAnAncestorsTriggerIsReported()
    {
        _model.Add("Newline", _root, _root, TriggerModel.Value(10));
        _model.Add(RunTransition(-1, TriggerModel.Any, _model.Ref(_payload), Run()));

        await Assert.That(Validate().Describe())
            .IsEqualTo("SALCH0702: Run 'Capture' shadows 'Newline' on 'Root': 10 is taken into the run instead of ending it; declare it on 'Payload' too");
    }

    /// <summary>The same value declared on the run's own state ends the run, which is the fix the warning asks for.</summary>
    [Test]
    public async Task ARunIsEndedByATriggerOnItsOwnState()
    {
        _model.Add("Newline", _payload, _root, TriggerModel.Value(10));
        _model.Add(RunTransition(-1, TriggerModel.Any, _model.Ref(_payload), Run()));

        await Assert.That(Validate().Describe()).IsEqualTo("");
    }

    [Test]
    public async Task ARunTakesItsStateAndTheRunFirst()
    {
        _model.Add(RunTransition(-1, TriggerModel.Any, Run(), _model.Ref(_payload)));
        await Assert.That(Validate().Describe())
            .IsEqualTo("SALCH0701: [Run] on 'Capture' is invalid: its Transform must take (ref Payload self, ReadOnlySpan<System.Byte> run), optionally followed by the configuration and the context");
    }
}
