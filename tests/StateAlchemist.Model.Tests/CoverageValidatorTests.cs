using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class CoverageValidatorTests
{
    [Test]
    public async Task ALeafWithoutOrElseLeavesTheRestUnhandled()
    {
        var model = new TestModel();
        var root = model.Root();
        var awaiting = model.State("Awaiting", root, initial: true);
        model.Add("Naws", awaiting, -1, TriggerModel.Value(31));
        model.Add("Low", root, -1, TriggerModel.Range(0, 9));
        var built = model.Build();
        await Assert.That(CoverageValidator.Validate(built, new Hierarchy(built.States)).Describe())
            .IsEqualTo("SALCH0501: Leaf 'Awaiting' leaves 245 values unhandled and no [OnAny] covers them");
    }

    [Test]
    public async Task AnOrElseOnAnAncestorCoversTheLeaf()
    {
        var model = new TestModel();
        var root = model.Root();
        var awaiting = model.State("Awaiting", root, initial: true);
        model.Add("Naws", awaiting, -1, TriggerModel.Value(31));
        model.Add("Skip", root, -1, TriggerModel.Any);
        var built = model.Build();
        await Assert.That(CoverageValidator.Validate(built, new Hierarchy(built.States)).Describe()).IsEqualTo("");
    }
}
