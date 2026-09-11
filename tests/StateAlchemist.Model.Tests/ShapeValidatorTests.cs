using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class ShapeValidatorTests
{
    private readonly TestModel _model = new();
    private readonly int _root;
    private readonly int _idle;

    public ShapeValidatorTests()
    {
        _root = _model.Root();
        _idle = _model.State("Idle", _root, initial: true);
    }

    private string Problems() => ShapeValidator.Validate(_model.Build()).Describe();

    private static TransitionModel ClassForm(string name, int source, int target, MethodModel? guard = null, MethodModel? transform = null,
        MethodModel[]? completed = null, DecisionModel? decision = null, string[]? unknown = null) =>
        new(0, name, source, target, TriggerModel.Value(1), 0, false, guard, transform, completed ?? [], decision, unknown ?? [], "T.Module", SourceSpan.None);

    [Test]
    public async Task AStateMustBeAPublicStructWithOneParent()
    {
        _model.Replace(_idle, new StateModel(_idle, "T.Idle", _root, true, false, false, IsPublicStruct: false, ParentMarkers: 1, SourceSpan.None));
        await Assert.That(Problems()).IsEqualTo("SALCH0001: State 'Idle' must be a public struct with exactly one parent marker; it is not a public struct");
    }

    [Test]
    public async Task EveryMethodMustBePublicStatic()
    {
        var hidden = new MethodModel("T.Module", "Hidden", ReturnShape.Void, IsStatic: true, IsPublic: false, [], SourceSpan.None);
        _model.Add(ClassForm("T.Module.Hidden", _idle, -1, transform: hidden));
        await Assert.That(Problems()).IsEqualTo("SALCH0002: 'T.Module.Hidden' must be public and static");
    }

    [Test]
    public async Task PhasesHaveFixedReturnTypes()
    {
        _model.Add(ClassForm("T.Go", _idle, -1,
            guard: TestModel.Method("T.Go", "Guard", ReturnShape.Void),
            transform: TestModel.Method("T.Go", "Transform", ReturnShape.Bool)));
        await Assert.That(Problems()).IsEqualTo(
            "SALCH0203: 'T.Go.Guard' must return bool synchronously\n" +
            "SALCH0203: 'T.Go.Transform' must return void synchronously");
    }

    [Test]
    public async Task TheAsyncSuffixMustMatchTheReturnType()
    {
        _model.Add(ClassForm("T.Go", _idle, -1, completed:
        [
            TestModel.Method("T.Go", "Completed", ReturnShape.ValueTask),
            TestModel.Method("T.Go", "CompletedAsync", ReturnShape.Void),
        ]));
        await Assert.That(Problems()).IsEqualTo(
            "SALCH0207: 'T.Go.Completed' returns a task, so it must be named 'CompletedAsync'\n" +
            "SALCH0207: 'T.Go.CompletedAsync' does not return a task, so it must be named 'Completed'");
    }

    [Test]
    public async Task AMethodThatIsNotAPhaseIsReported()
    {
        _model.Add(ClassForm("T.Go", _idle, -1, unknown: ["Helper", "TransformAsync"]));
        await Assert.That(Problems()).IsEqualTo(
            "SALCH0206: 'T.Go.Helper' is not a phase; a class-form transition may declare Guard, Transform, Decide, DecideAsync, Complete, Completed and CompletedAsync\n" +
            "SALCH0207: 'T.Go.TransformAsync' cannot be async: it runs before the state changes");
    }

    [Test]
    public async Task EveryOutcomeIsCompletedExactlyOnce()
    {
        var decide = TestModel.Method("T.Check", "DecideAsync", ReturnShape.ValueTaskOfResult);
        var accept = new OutcomeCompletion("T.Accept", _idle, TestModel.Method("T.Check", "Complete", ReturnShape.Void));
        var stray = new OutcomeCompletion("T.Maybe", _idle, TestModel.Method("T.Check", "Complete", ReturnShape.Void));
        var decision = new DecisionModel(null, decide, ["T.Accept", "T.Reject"], [accept, stray], []);
        _model.Add(ClassForm("T.Check", _idle, -1, decision: decision));
        await Assert.That(Problems()).IsEqualTo(
            "SALCH0401: Decision 'T.Check' has 0 Complete methods for outcome 'Reject'; it needs exactly one\n" +
            "SALCH0402: 'T.Check.Complete' completes 'Maybe', which is not an outcome of decision 'T.Check'");
    }

    [Test]
    public async Task ADecisionDeclaresOneDecideMethod()
    {
        var both = new DecisionModel(
            TestModel.Method("T.Check", "Decide", ReturnShape.Other),
            TestModel.Method("T.Check", "DecideAsync", ReturnShape.ValueTaskOfResult), [], [], []);
        _model.Add(ClassForm("T.Check", _idle, -1, decision: both));
        await Assert.That(Problems()).IsEqualTo("SALCH0208: Decision 'T.Check' declares both Decide and DecideAsync");
    }

    [Test]
    public async Task StrictPurityForbidsTheContextBeforeTheStateChanges()
    {
        _model.Purity = PurityMode.Strict;
        var context = new ParameterModel("context", "T.Context", ParameterKind.Context, Passing.Value);
        _model.Add(ClassForm("T.Go", _idle, -1,
            transform: TestModel.Method("T.Go", "Transform", ReturnShape.Void, context),
            completed: [TestModel.Method("T.Go", "Completed", ReturnShape.Void, context)]));
        await Assert.That(Problems()).IsEqualTo("SALCH0205: 'T.Go.Transform' takes the context, which machine 'TestMachine' forbids with Purity.Strict");
    }

    [Test]
    public async Task AnUncheckedMachineWithAsyncActionsIsWarned()
    {
        _model.Concurrency = ConcurrencyMode.Unchecked;
        _model.Add(ClassForm("T.Go", _idle, -1, completed: [TestModel.Method("T.Go", "CompletedAsync", ReturnShape.ValueTask)]));
        await Assert.That(Problems()).IsEqualTo("SALCH0209: Machine 'TestMachine' is Unchecked but has async actions or decisions; await every FireAsync before calling the next");
    }
}
