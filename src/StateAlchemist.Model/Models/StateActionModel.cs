namespace StateAlchemist.Model;

/// <summary>An <c>[Exited]</c> or <c>[Entered]</c> action.</summary>
/// <param name="Phase">When it runs.</param>
/// <param name="State">The state's index.</param>
/// <param name="Order">Its <c>Order</c>.</param>
/// <param name="Module">The declaring module's full name.</param>
/// <param name="DeclarationIndex">Its position among the module's declarations, for ordering within a module.</param>
/// <param name="Method">The method.</param>
public sealed record StateActionModel(ActionPhase Phase, int State, int Order, string Module, int DeclarationIndex, MethodModel Method);
