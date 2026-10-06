using System.Collections.Generic;

namespace StateAlchemist.Model;

/// <summary>
/// A transition, one per trigger: a method with <c>[On(1)]</c> and <c>[On(2)]</c> is two transition models
/// sharing its methods.
/// </summary>
/// <param name="Index">Its position in <see cref="MachineModel.Transitions"/>.</param>
/// <param name="Name">The declaring member, such as <c>TelnetCore.Refuse</c>.</param>
/// <param name="Source">The source state's index.</param>
/// <param name="Target">The target state's index, or −1 for a stay or a decision.</param>
/// <param name="Trigger">What it fires on.</param>
/// <param name="Order">Its order among guarded transitions for the same trigger.</param>
/// <param name="IsRun">Whether it is a run transition.</param>
/// <param name="Guard">Its <c>Guard</c>.</param>
/// <param name="Transform">Its <c>Transform</c>.</param>
/// <param name="Completed">Its <c>Completed</c>/<c>CompletedAsync</c> methods (several for a decision's outcomes).</param>
/// <param name="Decision">Its decision parts, if it is a decision.</param>
/// <param name="UnknownMembers">Class-form method names that are not phases.</param>
/// <param name="Module">The declaring module's full name.</param>
/// <param name="Location">Where it is declared.</param>
/// <param name="Join">For a join (<c>[OnAll]</c>), every event it waits for; this model is the one for <see cref="Trigger"/>'s event.</param>
/// <param name="History">Whether a move into a state with children enters what was last active there.</param>
public sealed record TransitionModel(
    int Index,
    string Name,
    int Source,
    int Target,
    TriggerModel Trigger,
    int Order,
    bool IsRun,
    MethodModel? Guard,
    MethodModel? Transform,
    IReadOnlyList<MethodModel> Completed,
    DecisionModel? Decision,
    IReadOnlyList<UnknownMember> UnknownMembers,
    string Module,
    SourceSpan Location,
    JoinModel? Join = null,
    HistoryKind History = HistoryKind.None)
{
    /// <summary>Stay, move or re-entry.</summary>
    public MoveKind Kind => Target < 0 ? MoveKind.Stay : Target == Source ? MoveKind.Reenter : MoveKind.Move;

    /// <summary>Whether it declares a guard.</summary>
    public bool IsGuarded => Guard is not null;

    /// <summary>Whether it is a decision.</summary>
    public bool IsDecision => Decision is not null;

    /// <summary>Whether it is a join.</summary>
    public bool IsJoin => Join is not null;

    /// <summary>Every method it declares.</summary>
    public IEnumerable<MethodModel> Methods
    {
        get
        {
            if (Guard is not null)
            {
                yield return Guard;
            }

            if (Transform is not null)
            {
                yield return Transform;
            }

            if (Decision?.Decide is not null)
            {
                yield return Decision.Decide;
            }

            if (Decision?.DecideAsync is not null)
            {
                yield return Decision.DecideAsync;
            }

            foreach (var completion in Decision?.Completions ?? [])
            {
                yield return completion.Complete;
            }

            foreach (var completed in Completed)
            {
                yield return completed;
            }
        }
    }
}

/// <summary>A class-form method whose name is not a phase, and where it is written if the front-end can tell.</summary>
/// <param name="Name">The member, qualified by its transition: <c>Finish.Complet</c>.</param>
/// <param name="Location">Where it is declared, for the diagnostic and its rename fix.</param>
public sealed record UnknownMember(string Name, SourceSpan? Location = null)
{
    /// <inheritdoc/>
    public override string ToString() => Name;
}

/// <summary>
/// A join: <c>[OnAll(typeof(A), typeof(B))]</c>. It is modelled as one transition per listed event, each sharing this,
/// so resolution, conflicts and roles treat every event as an ordinary event trigger.
/// </summary>
/// <param name="Events">The events' full names, as declared.</param>
public sealed record JoinModel(IReadOnlyList<string> Events)
{
    /// <summary>The most events one join may list: one bit each in its arrival mask.</summary>
    public const int MaxEvents = 32;

    /// <summary>The bit for <paramref name="eventType"/> in the arrival mask, or −1 if the join does not list it.</summary>
    public int BitOf(string eventType)
    {
        for (var i = 0; i < Events.Count; i++)
        {
            if (Events[i] == eventType)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// What is wrong with an <c>[OnAll]</c> listing <paramref name="count"/> types, or null. Both front-ends ask, so
    /// they word it the same.
    /// </summary>
    /// <param name="count">How many types it lists.</param>
    /// <param name="anyMissing">Whether some listed type could not be read.</param>
    /// <param name="names">The listed types' full names.</param>
    public static string? Problem(int count, bool anyMissing, IReadOnlyList<string> names)
    {
        if (anyMissing)
        {
            return "lists a type in [OnAll] that is not an event type";
        }

        if (count < 2)
        {
            return "has an [OnAll] with fewer than two events: use [OnEvent]";
        }

        if (count > MaxEvents)
        {
            return $"has an [OnAll] with more than {MaxEvents} events";
        }

        for (var i = 0; i < names.Count; i++)
        {
            for (var j = i + 1; j < names.Count; j++)
            {
                if (names[i] == names[j])
                {
                    return $"lists '{names[i]}' twice in [OnAll]";
                }
            }
        }

        return null;
    }

    /// <summary>The arrival mask once every event has arrived.</summary>
    public uint Full => Events.Count >= MaxEvents ? uint.MaxValue : (1u << Events.Count) - 1;
}
