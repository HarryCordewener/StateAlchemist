using System;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class MachineModelTests
{
    [Test]
    public async Task ItRejectsAMisnumberedState()
    {
        var misnumbered = new StateModel(3, "T.Root", -1, false, false, false, true, 1, SourceSpan.None);
        await Assert.That(() => new MachineModel("M", new MachineOptions("System.Byte", ValueDomain.Byte), [misnumbered], []))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ItFindsStatesByTypeName()
    {
        var model = new TestModel();
        model.Root();
        var child = model.State("Child", 0, initial: true);
        var built = model.Build();
        await Assert.That(built.IndexOf("T.Child")).IsEqualTo(child);
        await Assert.That(built.IndexOf("T.Missing")).IsEqualTo(-1);
    }
}
