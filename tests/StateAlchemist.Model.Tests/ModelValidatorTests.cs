using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class ModelValidatorTests
{
    [Test]
    public async Task ACompleteTelnetShapedModelHasNoErrors()
    {
        var model = new TestModel();
        var root = model.Root(hasData: true);
        var idle = model.State("Idle", root, initial: true, hasData: true);
        var sub = model.State("Sub", root, hasData: true);
        var awaiting = model.State("Awaiting", sub, initial: true);
        var naws = model.State("Naws", sub, hasData: true);
        model.Add("Text", idle, -1, TriggerModel.Any, model.Ref(idle), TestModel.Value());
        model.Add("Begin", idle, sub, TriggerModel.Value(250), model.In(idle));
        model.Add("Naws", awaiting, naws, TriggerModel.Value(31), model.In(awaiting), model.Ref(sub), model.Ref(naws));
        model.Add("Capture", naws, -1, TriggerModel.Any, model.Ref(naws), TestModel.Value());
        model.Add("Skip", sub, idle, TriggerModel.Any);
        model.Add("Done", naws, idle, TriggerModel.Value(240), model.In(naws), model.Ref(root));

        await Assert.That(ModelValidator.Validate(model.Build()).Describe()).IsEqualTo("");
    }

    [Test]
    public async Task AnInvalidTreeStopsTheChecksThatNeedOne()
    {
        var model = new TestModel();
        var root = model.Root();
        model.State("A", root);
        model.State("B", root);
        await Assert.That(ModelValidator.Validate(model.Build()).Ids()).IsEqualTo("SALCH0004");
    }
}
