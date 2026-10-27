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

    [Test]
    public async Task ARootFinalLeafAndALeafThatForwardsAreNotChecked()
    {
        var model = new TestModel();
        var root = model.Root();
        var idle = model.State("Idle", root, initial: true);
        var busy = model.State("Busy", root);
        var other = model.State("Other", root);
        var done = model.State("Done", root);
        model.Final(done);
        model.Add("All", idle, -1, TriggerModel.Any);
        model.InvokeForwarding(busy, "System.Byte");
        model.InvokeForwarding(other, "System.Int32");
        var built = model.Build();
        await Assert.That(CoverageValidator.Validate(built, new Hierarchy(built.States)).Describe())
            .IsEqualTo("SALCH0501: Leaf 'Other' leaves 256 values unhandled and no [OnAny] covers them");
    }
}
