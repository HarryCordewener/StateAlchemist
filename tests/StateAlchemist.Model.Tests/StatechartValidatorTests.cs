using System.Linq;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class StatechartValidatorTests
{
    private static string Validate(TestModel model)
    {
        var built = model.Build();
        return StatechartValidator.Validate(built, new Hierarchy(built.States)).Describe();
    }

    [Test]
    public async Task AFinalStateIsALeafWithNoTransitions()
    {
        var model = new TestModel();
        var root = model.Root();
        var idle = model.State("Idle", root, initial: true);
        var done = model.State("Done", root);
        var inner = model.State("Inner", done, initial: true);
        model.Final(done);
        model.Add("Leave", done, idle, TriggerModel.Value(1));
        model.Add("Go", idle, inner, TriggerModel.Value(2));

        await Assert.That(Validate(model)).IsEqualTo(
            "SALCH0005: Final state 'Done' has children: only a leaf can be final\n" +
            "SALCH0005: Final state 'Done' is the source of 'Leave': a final state has no transitions");
    }

    [Test]
    public async Task TheRootCannotBeFinal()
    {
        var model = new TestModel();
        var root = model.Root();
        model.State("Idle", root, initial: true);
        model.Final(root);

        await Assert.That(Validate(model)).IsEqualTo("SALCH0005: Final state 'Root' is the root: mark one of its children [Final] instead");
    }

    [Test]
    public async Task AnEventlessTransitionMustMove()
    {
        var model = new TestModel();
        var root = model.Root();
        var idle = model.State("Idle", root, initial: true);
        model.Add("Stays", idle, -1, TriggerModel.Always);
        model.Add("Again", idle, idle, TriggerModel.Always);

        await Assert.That(Validate(model)).IsEqualTo(
            "SALCH0110: Eventless transition 'Stays' stays: an eventless transition must move to a state, set To\n" +
            "SALCH0110: Eventless transition 'Again' re-enters its own state without a guard, so it would fire forever");
    }

    [Test]
    public async Task UnguardedEventlessTransitionsMayNotLoop()
    {
        var model = new TestModel();
        var root = model.Root();
        var a = model.State("A", root, initial: true);
        var b = model.State("B", root);
        var c = model.State("C", root);
        model.Add("AToB", a, b, TriggerModel.Always);
        model.Add("BToA", b, a, TriggerModel.Always);
        model.Add("CToA", c, a, TriggerModel.Always);

        await Assert.That(Validate(model)).IsEqualTo(
            "SALCH0110: Eventless transition 'AToB' is part of a loop of unguarded eventless transitions, A -> B -> A, which would fire forever");
    }

    [Test]
    public async Task AGuardedEventlessLoopIsLeftToRunTime()
    {
        var model = new TestModel();
        var root = model.Root();
        var a = model.State("A", root, initial: true);
        var b = model.State("B", root);
        model.Add("AToB", a, b, TriggerModel.Always, guarded: true, order: 0);
        model.Add("BToA", b, a, TriggerModel.Always);

        await Assert.That(Validate(model)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ADoneTransitionNeedsAFinalChild()
    {
        var model = new TestModel();
        var root = model.Root();
        var job = model.State("Job", root, initial: true);
        model.State("Working", job, initial: true);
        var review = model.State("Review", root);
        model.Add("JobDone", job, review, TriggerModel.Done);
        model.Add("RootDone", root, review, TriggerModel.Done);

        await Assert.That(Validate(model)).IsEqualTo(
            "SALCH0111: 'JobDone' fires when 'Job' is done, but 'Job' has no [Final] child and invokes no machine\n" +
            "SALCH0111: 'RootDone' fires when 'Root' is done, but 'Root' is the root, whose final children complete the machine instead");
    }

    [Test]
    public async Task AStateCannotHandleAndDeferTheSameEvent()
    {
        var model = new TestModel();
        var root = model.Root();
        var idle = model.State("Idle", root, initial: true);
        model.Defer(idle, "T.Ping", "T.Pong");
        model.Add("Pinged", idle, -1, TriggerModel.Event("T.Ping"));

        await Assert.That(Validate(model)).IsEqualTo("SALCH0112: State 'Idle' defers 'Ping': 'Pinged' handles it in the same state; handle it or defer it");
    }

    [Test]
    public async Task TheResolverTriesEventlessTransitionsNearestFirstAndDoneOnlyAtItsState()
    {
        var model = new TestModel();
        var root = model.Root();
        var job = model.State("Job", root, initial: true);
        var working = model.State("Working", job, initial: true);
        var other = model.State("Other", root);
        model.Add("Near", working, other, TriggerModel.Always, guarded: true, order: 0);
        model.Add("Far", job, other, TriggerModel.Always);
        model.Add("Outer", root, other, TriggerModel.Always);
        model.Add("JobDone", job, other, TriggerModel.Done);
        var built = model.Build();
        var resolver = new Resolver(built, new Hierarchy(built.States));

        await Assert.That(string.Join(",", resolver.ForAlways(working).Select(t => t.Name))).IsEqualTo("Near,Far");
        await Assert.That(string.Join(",", resolver.ForDone(job).Select(t => t.Name))).IsEqualTo("JobDone");
        await Assert.That(resolver.ForDone(working).Count).IsEqualTo(0);
    }

    [Test]
    public async Task TheFirstStateThatHandlesOrDefersAnEventDecides()
    {
        var model = new TestModel();
        var root = model.Root();
        var job = model.State("Job", root, initial: true);
        var working = model.State("Working", job, initial: true);
        model.Defer(job, "T.Ping");
        model.Add("Guarded", working, -1, TriggerModel.Event("T.Ping"), guarded: true, order: 0);
        model.Add("Outer", root, -1, TriggerModel.Event("T.Ping"));
        var built = model.Build();
        var resolver = new Resolver(built, new Hierarchy(built.States));

        var candidates = resolver.ForEvent(working, "T.Ping", out var deferred);
        await Assert.That(string.Join(",", candidates.Select(t => t.Name))).IsEqualTo("Guarded");
        await Assert.That(deferred).IsTrue();
    }

    [Test]
    public async Task AnInvokeMethodReturnsAStatechartAndTakesWhatAnEnteredActionCould()
    {
        var model = new TestModel();
        var root = model.Root();
        var idle = model.State("Idle", root, initial: true);
        var busy = model.State("Busy", root);
        model.Add("Go", idle, busy, TriggerModel.Value(1));
        model.Invoke(busy, null);
        model.Invoke(busy, "System.Byte", model.In(busy), model.In(idle), model.Ref(root), TestModel.Value());
        model.Invoke(busy, "System.Byte", model.In(root), new ParameterModel("context", "T.Context", ParameterKind.Context, Passing.Value));

        await Assert.That(Validate(model)).IsEqualTo(
            "SALCH0113: [Invoke] 'T.Module.Child0' must return a type that implements IStatechart<TValue>\n" +
            "SALCH0113: [Invoke] 'T.Module.Child1' takes 'idle', but 'Idle' is not active in 'Busy'\n" +
            "SALCH0113: [Invoke] 'T.Module.Child1' takes 'root' by ref: it reads states and the configuration, take them by in\n" +
            "SALCH0113: [Invoke] 'T.Module.Child1' takes 'value': it may take only active states, the context, the configuration and a CancellationToken");
    }
}
