using System.Linq;
using System.Threading.Tasks;
using TUnit.Core;

namespace StateAlchemist.Model.Tests;

/// <summary>
/// Root ─┬─ Idle [Initial]
///       └─ Sub ─┬─ Awaiting [Initial]
///               └─ Naws
/// </summary>
public class HierarchyTests
{
    private static (Hierarchy Tree, int Root, int Idle, int Sub, int Awaiting, int Naws) Telnet()
    {
        var model = new TestModel();
        var root = model.Root();
        var idle = model.State("Idle", root, initial: true);
        var sub = model.State("Sub", root);
        var awaiting = model.State("Awaiting", sub, initial: true);
        var naws = model.State("Naws", sub);
        return (new Hierarchy(model.Build().States), root, idle, sub, awaiting, naws);
    }

    [Test]
    public async Task TheLowestCommonAncestorOfCousinsIsTheirSharedParent()
    {
        var (tree, root, idle, sub, awaiting, naws) = Telnet();
        await Assert.That(tree.LowestCommonAncestor(awaiting, naws)).IsEqualTo(sub);
        await Assert.That(tree.LowestCommonAncestor(idle, naws)).IsEqualTo(root);
        await Assert.That(tree.LowestCommonAncestor(naws, sub)).IsEqualTo(sub);
    }

    [Test]
    public async Task EnteringAParentContinuesToItsInitialLeaf()
    {
        var (tree, root, idle, sub, awaiting, _) = Telnet();
        await Assert.That(tree.InitialLeaf(sub)).IsEqualTo(awaiting);
        await Assert.That(tree.InitialLeaf(root)).IsEqualTo(idle);
    }

    [Test]
    public async Task DepthPathAndLeavesFollowTheTree()
    {
        var (tree, root, idle, sub, awaiting, naws) = Telnet();
        await Assert.That(tree.Depth(naws)).IsEqualTo(2);
        await Assert.That(string.Join(",", tree.PathFromRoot(naws))).IsEqualTo($"{root},{sub},{naws}");
        await Assert.That(string.Join(",", tree.Leaves)).IsEqualTo($"{idle},{awaiting},{naws}");
        await Assert.That(string.Join(",", tree.LeavesUnder(sub))).IsEqualTo($"{awaiting},{naws}");
        await Assert.That(tree.IsAncestorOrSelf(sub, naws)).IsTrue();
        await Assert.That(tree.IsAncestorOrSelf(idle, naws)).IsFalse();
    }
}
