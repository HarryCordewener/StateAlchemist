using System.Collections.Generic;

namespace StateAlchemist.Model;

/// <summary>Which states one transition exits and enters, from one active leaf.</summary>
/// <param name="Leaf">The active leaf.</param>
/// <param name="TargetLeaf">The leaf the machine ends in.</param>
/// <param name="Lca">The lowest common ancestor: it and everything above it stays.</param>
/// <param name="Exiting">States exited, innermost first.</param>
/// <param name="Entering">States entered, outermost first.</param>
public sealed record TransitionPath(int Leaf, int TargetLeaf, int Lca, IReadOnlyList<int> Exiting, IReadOnlyList<int> Entering);

/// <summary>
/// Plans a transition's exits and entries (spec §6.2, §6.3). The rules:
/// <list type="bullet">
/// <item>A stay exits and enters nothing.</item>
/// <item>A move exits from the active leaf up to the lowest common ancestor of the leaf and the target, then enters
/// down to the target and on through its <c>[Initial]</c> children to a leaf.</item>
/// <item>A move to a state on the active path (the leaf itself or one of its ancestors) starts that state over: the
/// ancestor is its parent, so the target is exited and entered again. A re-entry is this case.</item>
/// <item>A move to the root never exits the root: everything below it is exited and the root's initial path entered.</item>
/// </list>
/// </summary>
public static class PathPlanner
{
    /// <summary>The path of <paramref name="transition"/> fired from <paramref name="leaf"/>.</summary>
    public static TransitionPath Plan(Hierarchy hierarchy, TransitionModel transition, int leaf) =>
        transition.Kind == MoveKind.Stay || transition.IsDecision
            ? Stay(leaf)
            : Move(hierarchy, leaf, transition.Target);

    /// <summary>A stay at <paramref name="leaf"/>.</summary>
    public static TransitionPath Stay(int leaf) => new(leaf, leaf, leaf, [], []);

    /// <summary>A move from <paramref name="leaf"/> to <paramref name="target"/>.</summary>
    public static TransitionPath Move(Hierarchy hierarchy, int leaf, int target)
    {
        int lca;
        if (target == hierarchy.Root)
        {
            lca = hierarchy.Root;
        }
        else if (hierarchy.IsAncestorOrSelf(target, leaf))
        {
            lca = hierarchy.ParentOf(target);
        }
        else
        {
            lca = hierarchy.LowestCommonAncestor(leaf, target);
        }

        var exiting = new List<int>();
        for (var at = leaf; at != lca; at = hierarchy.ParentOf(at))
        {
            exiting.Add(at);
        }

        var targetLeaf = hierarchy.InitialLeaf(target);
        var entering = new List<int>();
        for (var at = targetLeaf; at != lca; at = hierarchy.ParentOf(at))
        {
            entering.Add(at);
        }

        entering.Reverse();
        return new TransitionPath(leaf, targetLeaf, lca, exiting, entering);
    }
}
