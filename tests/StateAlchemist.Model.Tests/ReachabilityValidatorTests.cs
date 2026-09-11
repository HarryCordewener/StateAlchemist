using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class ReachabilityValidatorTests
{
    [Test]
    public async Task AStateNoTransitionEntersIsUnreachable()
    {
        var model = new TestModel();
        var root = model.Root();
        var idle = model.State("Idle", root, initial: true);
        var command = model.State("Command", root);
        model.State("Orphan", root);
        model.Add("Begin", idle, command, TriggerModel.Value(255));
        var built = model.Build();
        await Assert.That(ReachabilityValidator.Validate(built, new Hierarchy(built.States)).Describe())
            .IsEqualTo("SALCH0502: State 'Orphan' cannot be reached from the initial state");
    }
}
