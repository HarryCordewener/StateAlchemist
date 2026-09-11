using System.Linq;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class StopSetsTests
{
    [Test]
    public async Task ARunStopsAtEveryValueSomethingElseHandlesFirst()
    {
        var model = new TestModel();
        var root = model.Root();
        var payload = model.State("Payload", root, initial: true);
        var run = model.Add(new TransitionModel(0, "Capture", payload, -1, TriggerModel.Any, 0, IsRun: true, null,
            TestModel.Method("Capture", "Transform", ReturnShape.Void), [], null, [], "T.Module", SourceSpan.None));
        model.Add("Escape", payload, root, TriggerModel.Value(255));
        model.Add("RootValue", root, -1, TriggerModel.Value(10));      // shadowed by the run's [OnAny]
        var built = model.Build();
        var hierarchy = new Hierarchy(built.States);

        var stops = StopSets.For(new Resolver(built, hierarchy), built.Options, payload, run);

        await Assert.That(string.Join(",", stops)).IsEqualTo("255");
    }
}
