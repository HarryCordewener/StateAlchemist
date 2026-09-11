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
    SourceSpan Location)
{
    /// <summary>Stay, move or re-entry.</summary>
    public MoveKind Kind => Target < 0 ? MoveKind.Stay : Target == Source ? MoveKind.Reenter : MoveKind.Move;

    /// <summary>Whether it declares a guard.</summary>
    public bool IsGuarded => Guard is not null;

    /// <summary>Whether it is a decision.</summary>
    public bool IsDecision => Decision is not null;

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
