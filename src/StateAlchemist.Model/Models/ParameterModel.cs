namespace StateAlchemist.Model;

/// <summary>A parameter of a transition, decision or action method.</summary>
/// <param name="Name">The parameter's name.</param>
/// <param name="TypeName">Its type's full name.</param>
/// <param name="Kind">What it binds to.</param>
/// <param name="Passing">How it is passed.</param>
/// <param name="State">For <see cref="ParameterKind.State"/>, the state's index; otherwise −1.</param>
public sealed record ParameterModel(string Name, string TypeName, ParameterKind Kind, Passing Passing, int State = -1, SourceSpan? Location = null);
