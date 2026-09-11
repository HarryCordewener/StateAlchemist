using System.Collections.Generic;

namespace StateAlchemist.Model;

/// <summary>A state's role in a planned transition (spec §6.3).</summary>
public static class Roles
{
    /// <summary>
    /// Exiting if the path exits it, entering if it enters it (a state started over is both), staying if it is the
    /// lowest common ancestor or above; otherwise none.
    /// </summary>
    public static Role Of(Hierarchy hierarchy, TransitionPath path, int state)
    {
        var role = Role.None;
        if (Contains(path.Exiting, state))
        {
            role |= Role.Exiting;
        }

        if (Contains(path.Entering, state))
        {
            role |= Role.Entering;
        }

        if (hierarchy.IsAncestorOrSelf(state, path.Lca))
        {
            role |= Role.Staying;
        }

        return role;
    }

    private static bool Contains(IReadOnlyList<int> states, int state)
    {
        foreach (var candidate in states)
        {
            if (candidate == state)
            {
                return true;
            }
        }

        return false;
    }
}
