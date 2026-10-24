namespace StateAlchemist.Model;

/// <summary>An <c>[Invoke]</c> method: it creates a child machine that runs while its state is active.</summary>
/// <param name="State">The state's index.</param>
/// <param name="Method">The method that creates the child.</param>
/// <param name="Forward">Whether the triggers the parent does not handle go to the child.</param>
/// <param name="ChildValueType">The child's value-trigger type, by full name: the <c>TValue</c> of the
/// <c>IStatechart&lt;TValue&gt;</c> the method returns; <see langword="null"/> when it returns something else.</param>
/// <param name="Module">The declaring module's full name.</param>
/// <param name="DeclarationIndex">Its position among the module's declarations.</param>
public sealed record InvokeModel(int State, MethodModel Method, bool Forward, string? ChildValueType, string Module, int DeclarationIndex);
