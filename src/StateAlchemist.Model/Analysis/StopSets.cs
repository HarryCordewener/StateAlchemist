using System.Collections.Generic;
using System.Linq;

namespace StateAlchemist.Model;

/// <summary>Where a run ends: the values some other transition handles first (spec §6.7).</summary>
public static class StopSets
{
    /// <summary>
    /// Every value for which resolution from <paramref name="leaf"/> does not try <paramref name="run"/> first. The
    /// scan hands the run everything up to the first such value.
    /// </summary>
    public static IReadOnlyList<long> For(Resolver resolver, MachineOptions options, int leaf, TransitionModel run) =>
        options.Domain.Values
            .Where(value =>
            {
                var candidates = resolver.ForValue(leaf, value);
                return candidates.Count == 0 || candidates[0].Index != run.Index;
            })
            .ToList();
}
