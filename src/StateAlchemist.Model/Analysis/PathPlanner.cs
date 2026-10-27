using System.Collections.Generic;
using System.Linq;

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
/// <item>A move with history enters, instead of the target's <c>[Initial]</c> path, the leaf that was active when the
/// target was last exited (deep), or the <c>[Initial]</c> path of the child that was (shallow).</item>
/// </list>
/// </summary>
public static class PathPlanner
{
    /// <summary>
    /// The path of <paramref name="transition"/> fired from <paramref name="leaf"/>. For a move with history that
    /// depends on what was recorded, the path taken before anything is: see <see cref="Plans"/>.
    /// </summary>
    public static TransitionPath Plan(Hierarchy hierarchy, TransitionModel transition, int leaf) => Plans(hierarchy, transition, leaf)[0];

    /// <summary>
    /// Every path <paramref name="transition"/> can take from <paramref name="leaf"/>: one, unless it is a move with
    /// history whose target is not on the active path. Then one per leaf it can recall, the <c>[Initial]</c> one first.
    /// </summary>
    public static IReadOnlyList<TransitionPath> Plans(Hierarchy hierarchy, TransitionModel transition, int leaf) =>
        transition.Kind == MoveKind.Stay || transition.IsDecision
            ? [Stay(leaf)]
            : Moves(hierarchy, leaf, transition.Target, transition.History);

    /// <summary>A stay at <paramref name="leaf"/>.</summary>
    public static TransitionPath Stay(int leaf) => new(leaf, leaf, leaf, [], []);

    /// <summary>A move from <paramref name="leaf"/> to <paramref name="target"/> that enters its <c>[Initial]</c> path.</summary>
    public static TransitionPath Move(Hierarchy hierarchy, int leaf, int target) => Move(hierarchy, leaf, target, hierarchy.InitialLeaf(target));

    /// <summary>
    /// A move from <paramref name="leaf"/> to <paramref name="target"/> with <paramref name="history"/>, where
    /// <paramref name="recorded"/> is the leaf that was active when the target was last exited, or −1 for none. A
    /// target on the active path is exited by the move itself, so what it recalls is <paramref name="leaf"/>.
    /// </summary>
    public static TransitionPath Move(Hierarchy hierarchy, int leaf, int target, HistoryKind history, int recorded)
    {
        if (history == HistoryKind.None || target == hierarchy.Root)
        {
            return Move(hierarchy, leaf, target);
        }

        var last = hierarchy.IsAncestorOrSelf(target, leaf) ? leaf : recorded;
        return Move(hierarchy, leaf, target, Recall(hierarchy, target, history, last));
    }

    /// <summary>
    /// Every path a move from <paramref name="leaf"/> to <paramref name="target"/> with <paramref name="history"/>
    /// can take: one, unless what it enters depends on what was recorded (<see cref="RecallsAtRunTime"/>).
    /// </summary>
    public static IReadOnlyList<TransitionPath> Moves(Hierarchy hierarchy, int leaf, int target, HistoryKind history) =>
        RecallsAtRunTime(hierarchy, leaf, target, history)
            ? Recallable(hierarchy, target, history).Select(targetLeaf => Move(hierarchy, leaf, target, targetLeaf)).ToList()
            : [Move(hierarchy, leaf, target, history, -1)];

    /// <summary>
    /// Whether a move from <paramref name="leaf"/> to <paramref name="target"/> with <paramref name="history"/> enters
    /// a leaf that is known only when it runs: the target has children and is not on the active path.
    /// </summary>
    public static bool RecallsAtRunTime(Hierarchy hierarchy, int leaf, int target, HistoryKind history) =>
        history != HistoryKind.None && target != hierarchy.Root && !hierarchy.IsLeaf(target) && !hierarchy.IsAncestorOrSelf(target, leaf);

    /// <summary>
    /// The leaf a move to <paramref name="target"/> with <paramref name="history"/> enters, when
    /// <paramref name="recorded"/> was active as the target was last exited. Anything that is not a leaf under the
    /// target, such as −1, means nothing was recorded: the <c>[Initial]</c> path.
    /// </summary>
    public static int Recall(Hierarchy hierarchy, int target, HistoryKind history, int recorded)
    {
        if (history == HistoryKind.None || recorded < 0 || recorded >= hierarchy.Count || recorded == target ||
            !hierarchy.IsLeaf(recorded) || !hierarchy.IsAncestorOrSelf(target, recorded))
        {
            return hierarchy.InitialLeaf(target);
        }

        if (history == HistoryKind.Deep)
        {
            return recorded;
        }

        var child = recorded;
        while (hierarchy.ParentOf(child) != target)
        {
            child = hierarchy.ParentOf(child);
        }

        return hierarchy.InitialLeaf(child);
    }

    /// <summary>Every leaf a move to <paramref name="target"/> with <paramref name="history"/> can enter: the <c>[Initial]</c> leaf first, then by index.</summary>
    public static IReadOnlyList<int> Recallable(Hierarchy hierarchy, int target, HistoryKind history)
    {
        var leaves = new List<int> { hierarchy.InitialLeaf(target) };
        if (history == HistoryKind.None || target == hierarchy.Root)
        {
            return leaves;
        }

        foreach (var leaf in hierarchy.LeavesUnder(target))
        {
            var recalled = Recall(hierarchy, target, history, leaf);
            if (!leaves.Contains(recalled))
            {
                leaves.Add(recalled);
            }
        }

        return leaves;
    }

    /// <summary>A move from <paramref name="leaf"/> to <paramref name="target"/> that ends in <paramref name="targetLeaf"/>, a leaf at or under the target.</summary>
    public static TransitionPath Move(Hierarchy hierarchy, int leaf, int target, int targetLeaf)
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

        var entering = new List<int>();
        for (var at = targetLeaf; at != lca; at = hierarchy.ParentOf(at))
        {
            entering.Add(at);
        }

        entering.Reverse();
        return new TransitionPath(leaf, targetLeaf, lca, exiting, entering);
    }
}
