using System.Collections.Generic;

namespace StateAlchemist.Model;

/// <summary>One outcome's completion: its <c>Complete</c> overload and target.</summary>
/// <param name="OutcomeType">The outcome case's full type name.</param>
/// <param name="Target">The target state's index.</param>
/// <param name="Complete">The <c>Complete</c> overload.</param>
public sealed record OutcomeCompletion(string OutcomeType, int Target, MethodModel Complete);

/// <summary>A decision's parts (spec §5.6).</summary>
/// <param name="Decide">A synchronous <c>Decide</c>, if declared.</param>
/// <param name="DecideAsync">An asynchronous <c>DecideAsync</c>, if declared.</param>
/// <param name="Outcomes">The outcome union's case types.</param>
/// <param name="Completions">The <c>Complete</c> overloads.</param>
/// <param name="Handle">Event types the pending state handles immediately.</param>
public sealed record DecisionModel(
    MethodModel? Decide,
    MethodModel? DecideAsync,
    IReadOnlyList<string> Outcomes,
    IReadOnlyList<OutcomeCompletion> Completions,
    IReadOnlyList<string> Handle)
{
    /// <summary>Whichever decide method is declared.</summary>
    public MethodModel? Decider => Decide ?? DecideAsync;
}
