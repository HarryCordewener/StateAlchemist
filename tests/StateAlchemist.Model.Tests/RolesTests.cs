using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

public class RolesTests
{
    [Test]
    public async Task EveryStateOnAMoveHasTheRoleItsPositionGivesIt()
    {
        var model = new TestModel();
        var root = model.Root();
        var idle = model.State("Idle", root, initial: true);
        var sub = model.State("Sub", root);
        var awaiting = model.State("Awaiting", sub, initial: true);
        var naws = model.State("Naws", sub);
        var tree = new Hierarchy(model.Build().States);

        var path = PathPlanner.Move(tree, awaiting, naws);
        await Assert.That(Roles.Of(tree, path, awaiting)).IsEqualTo(Role.Exiting);
        await Assert.That(Roles.Of(tree, path, sub)).IsEqualTo(Role.Staying);
        await Assert.That(Roles.Of(tree, path, root)).IsEqualTo(Role.Staying);
        await Assert.That(Roles.Of(tree, path, naws)).IsEqualTo(Role.Entering);
        await Assert.That(Roles.Of(tree, path, idle)).IsEqualTo(Role.None);

        var restart = PathPlanner.Move(tree, naws, sub);
        await Assert.That(Roles.Of(tree, restart, sub)).IsEqualTo(Role.Exiting | Role.Entering);
    }
}
