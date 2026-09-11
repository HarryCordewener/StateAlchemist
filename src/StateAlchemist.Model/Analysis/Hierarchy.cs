using System;
using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>
/// The state tree of a machine whose hierarchy is valid (run <see cref="HierarchyValidator"/> first): parents,
/// children, depths, lowest common ancestors and initial leaves.
/// </summary>
public sealed class Hierarchy
{
    private readonly IReadOnlyList<StateModel> _states;
    private readonly List<int>[] _children;
    private readonly int[] _depth;

    /// <summary>Builds the tree.</summary>
    /// <exception cref="InvalidOperationException">There is not exactly one root, or a parent chain loops.</exception>
    public Hierarchy(IReadOnlyList<StateModel> states)
    {
        _states = states;
        _children = new List<int>[states.Count];
        for (var i = 0; i < states.Count; i++)
        {
            _children[i] = [];
        }

        var roots = new List<int>();
        foreach (var state in states)
        {
            if (state.IsRoot)
            {
                roots.Add(state.Index);
            }
            else
            {
                _children[state.Parent].Add(state.Index);
            }
        }

        if (roots.Count != 1)
        {
            throw new InvalidOperationException($"A hierarchy needs exactly one root; found {roots.Count}. Run HierarchyValidator first.");
        }

        Root = roots[0];
        _depth = new int[states.Count];
        for (var i = 0; i < states.Count; i++)
        {
            var depth = 0;
            for (var at = states[i].Parent; at >= 0; at = states[at].Parent)
            {
                if (++depth > states.Count)
                {
                    throw new InvalidOperationException("A parent chain loops. Run HierarchyValidator first.");
                }
            }

            _depth[i] = depth;
        }
    }

    /// <summary>The root's index.</summary>
    public int Root { get; }

    /// <summary>How many states there are.</summary>
    public int Count => _states.Count;

    /// <summary>Every leaf, in index order.</summary>
    public IEnumerable<int> Leaves => Enumerable.Range(0, Count).Where(IsLeaf);

    /// <summary>The parent's index, or −1 for the root.</summary>
    public int ParentOf(int state) => _states[state].Parent;

    /// <summary>The children, in index order.</summary>
    public IReadOnlyList<int> ChildrenOf(int state) => _children[state];

    /// <summary>Whether the state has no children.</summary>
    public bool IsLeaf(int state) => _children[state].Count == 0;

    /// <summary>How many ancestors the state has; the root's depth is 0.</summary>
    public int Depth(int state) => _depth[state];

    /// <summary>Whether <paramref name="ancestor"/> is <paramref name="state"/> or one of its ancestors.</summary>
    public bool IsAncestorOrSelf(int ancestor, int state)
    {
        for (var at = state; at >= 0; at = _states[at].Parent)
        {
            if (at == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The deepest state that is an ancestor-or-self of both.</summary>
    public int LowestCommonAncestor(int a, int b)
    {
        while (_depth[a] > _depth[b])
        {
            a = _states[a].Parent;
        }

        while (_depth[b] > _depth[a])
        {
            b = _states[b].Parent;
        }

        while (a != b)
        {
            a = _states[a].Parent;
            b = _states[b].Parent;
        }

        return a;
    }

    /// <summary>The root, then each state down to <paramref name="state"/>.</summary>
    public IReadOnlyList<int> PathFromRoot(int state)
    {
        var path = new List<int>();
        for (var at = state; at >= 0; at = _states[at].Parent)
        {
            path.Add(at);
        }

        path.Reverse();
        return path;
    }

    /// <summary>The state's <c>[Initial]</c> child.</summary>
    /// <exception cref="InvalidOperationException">It has children but none is initial.</exception>
    public int InitialChildOf(int state)
    {
        foreach (var child in _children[state])
        {
            if (_states[child].IsInitial)
            {
                return child;
            }
        }

        throw new InvalidOperationException($"State '{_states[state].Name}' has no [Initial] child. Run HierarchyValidator first.");
    }

    /// <summary>The leaf reached by entering <paramref name="state"/>: down its <c>[Initial]</c> children.</summary>
    public int InitialLeaf(int state)
    {
        while (!IsLeaf(state))
        {
            state = InitialChildOf(state);
        }

        return state;
    }

    /// <summary>Every leaf at or beneath <paramref name="state"/>.</summary>
    public IEnumerable<int> LeavesUnder(int state) => Leaves.Where(leaf => IsAncestorOrSelf(state, leaf));
}
