using System.Collections.Generic;

namespace StateAlchemist;

/// <summary>
/// The method names a class-form transition or decision may declare. Imperative names run before the state
/// changes; past-tense names run after it. A name ending in <c>Async</c> returns <c>ValueTask</c> (decision D17).
/// </summary>
public static class PhaseNames
{
    /// <summary>May this transition fire? <c>static bool</c>, read-only.</summary>
    public const string Guard = "Guard";

    /// <summary>Turn the source into the target. <c>static void</c>, synchronous.</summary>
    public const string Transform = "Transform";

    /// <summary>Choose a decision's outcome, synchronously. Returns the outcome union.</summary>
    public const string Decide = "Decide";

    /// <summary>Choose a decision's outcome, asynchronously. Returns <c>ValueTask&lt;TOutcome&gt;</c>.</summary>
    public const string DecideAsync = "DecideAsync";

    /// <summary>A decision outcome's transform. <c>static void</c>, synchronous, marked <see cref="ToAttribute"/>.</summary>
    public const string Complete = "Complete";

    /// <summary>After the state has changed and every state action has run. <c>void</c>.</summary>
    public const string Completed = "Completed";

    /// <summary>After the state has changed and every state action has run. <c>ValueTask</c>.</summary>
    public const string CompletedAsync = "CompletedAsync";

    /// <summary>Every phase name, imperative first.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Guard, Transform, Decide, DecideAsync, Complete, Completed, CompletedAsync];
}
